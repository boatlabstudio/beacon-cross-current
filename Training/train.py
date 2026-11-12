"""Train PPO and export real checkpoint trajectories for the Unity replay."""

import argparse
from collections import Counter
import json
import os
from pathlib import Path

os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("OMP_NUM_THREADS", "1")

import numpy as np

from harbor_host import DEFAULT_HOST, HarborBatch
from ppo import Policy, gae


ROOT = Path(__file__).resolve().parent.parent
MISSIONS = ("gate", "current", "slalom", "dock")
OUTCOMES = {1: "pass", 2: "collision", 3: "miss", 4: "timeout"}
# These seeds are outside the training seed range. Every checkpoint sees the same tests.
TEST_SEEDS = np.arange(2_000_000_100, 2_000_000_148, dtype=np.int64)
REEL_COUNT = 6
ACTION_REPEAT = 4


def evaluate(policy, mission, host, steps, phase, record_signals=False):
    count = len(TEST_SEEDS)
    with HarborBatch(mission, count, host) as env:
        initial = env.request("reset", TEST_SEEDS)
        obs = initial["observations"]
        frames = [[pose] for pose in initial["poses"][:REEL_COUNT]]
        signals = [[] for _ in frames]
        coasts = [[] for _ in frames]
        post_pass_ticks = 28 if record_signals and phase == "late" and mission == "current" else 0
        outcomes = np.zeros(count, dtype=np.int32)
        for _ in range(251):
            actions = policy.mean(obs)
            result = env.request("step", TEST_SEEDS, actions, post_pass_ticks=post_pass_ticks)
            for i in range(REEL_COUNT):
                if outcomes[i] == 0:
                    if record_signals:
                        signals[i].append(dict(tick=frames[i][-1]["tick"],
                                               observation=obs[i].tolist(), action=actions[i].tolist()))
                    if post_pass_ticks and result["coasts"][i] is not None:
                        coasts[i] = result["coasts"][i]
                    frames[i].append(result["poses"][i])
            obs = result["observations"]
            pending = outcomes == 0
            outcomes[pending] = np.asarray(result["results"])[pending]
            if np.all(outcomes):
                break
        if not np.all(outcomes):
            raise RuntimeError("Evaluation exceeded the environment episode limit.")
    statistics = Counter(OUTCOMES[int(x)] for x in outcomes)
    attempts = [
        dict(id=i + 1, seed=int(TEST_SEEDS[i]), phase=phase, trainingSteps=steps,
             outcome=int(outcomes[i]), currentX=initial["currents"][i][0],
             currentZ=initial["currents"][i][1], frames=frames[i])
        for i in range(REEL_COUNT)
    ]
    if record_signals:
        for attempt, decisions, coast in zip(attempts, signals, coasts):
            attempt["signals"] = decisions
            if coast:
                attempt["coastFrames"] = coast
    return {"steps": steps, "phase": phase, "episodes": count, **statistics}, attempts


class TrainingTrace:
    """Record completed episodes in simulation step order, with environment index as the tie-break."""

    def __init__(self, initial, seeds):
        self.episodes = []
        self.attempts = []
        self.first_pass = None
        self.step = 0
        self.active = [
            dict(seed=int(seed), currentX=current[0], currentZ=current[1], frames=[pose])
            for seed, current, pose in zip(seeds, initial["currents"], initial["poses"])
        ]

    def record(self, result, next_seeds):
        self.step += 1
        for episode, pose in zip(self.active, result["poses"]):
            episode["frames"].append(pose)
        finished = sorted(
            (i for i, outcome in enumerate(result["results"]) if outcome),
            key=lambda i: ((result["poses"][i]["tick"] - 1) % ACTION_REPEAT, i),
        )
        for i in finished:
            episode = self.active[i]
            outcome = result["results"][i]
            number = len(self.episodes) + 1
            episode.update(id=number, outcome=outcome, phase="training",
                           trainingSteps=self.step * len(self.active))
            self.episodes.append(dict(id=number, outcome=outcome, environment=i,
                                      ticks=episode["frames"][-1]["tick"], steps=episode["trainingSteps"]))
            is_first_pass = outcome == 1 and self.first_pass is None
            if number <= 240 or is_first_pass:
                self.attempts.append(episode)
            if is_first_pass:
                self.first_pass = number
                print(f"First training pass: attempt {number}", flush=True)
            reset = result["resets"][i]
            self.active[i] = dict(seed=int(next_seeds[i]), currentX=reset["currentX"],
                                  currentZ=reset["currentZ"], frames=[reset["pose"]])


def rollout(env, policy, obs, rng, horizon, curriculum=False, trace=None):
    count, width = obs.shape
    shape = (horizon, count)
    inputs = np.empty((*shape, width), dtype=np.float32)
    actions = np.empty((*shape, 2), dtype=np.float32)
    old_logp = np.empty(shape, dtype=np.float32)
    values = np.empty(shape, dtype=np.float32)
    rewards = np.empty(shape, dtype=np.float32)
    ends = np.empty(shape, dtype=np.float32)
    outcomes = Counter()
    for t in range(horizon):
        inputs[t] = obs
        actions[t], old_logp[t], values[t] = policy.sample(obs)
        seeds = rng.integers(1, 1_000_000_000, count)
        starts = rng.choice(3, count, p=(0.46, 0.30, 0.24)) if curriculum else None
        result = env.request("step", seeds, actions[t], starts, record_resets=trace is not None)
        if trace is not None:
            trace.record(result, seeds)
        obs = result["observations"]
        rewards[t] = result["rewards"]
        ends[t] = np.asarray(result["results"]) != 0
        outcomes.update(OUTCOMES[x] for x in result["results"] if x)
    advantages, returns = gae(rewards, ends, values, policy.values(obs))
    batch = (inputs.reshape(-1, width), actions.reshape(-1, 2), old_logp.ravel(),
             advantages.ravel(), returns.ravel())
    return obs, batch, outcomes


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    staging = path.with_suffix(".tmp")
    staging.write_text(json.dumps(value, separators=(",", ":")) + "\n", encoding="utf-8")
    staging.replace(path)


def train(args):
    if not args.host.is_file():
        raise SystemExit("Build HarborHost first: dotnet build Training/HarborHost -c Release")
    rng = np.random.default_rng(args.seed)
    evaluations, attempts = [], []
    out_dir = args.output / args.mission
    out_dir.mkdir(parents=True, exist_ok=True)
    stride = args.envs * args.horizon
    updates = (args.steps + stride - 1) // stride
    checkpoint_updates = {0: "early", updates: "late"}
    if updates >= 4:
        checkpoint_updates[updates // 2] = "middle"
    if updates >= 8:
        checkpoint_updates[updates // 4] = "learning"
    trace = None

    def checkpoint(update):
        steps = update * stride
        phase = checkpoint_updates[update]
        statistics, clips = evaluate(policy, args.mission, args.host, steps, phase, trace is not None)
        if trace is not None:
            statistics["completedTrainingEpisodes"] = len(trace.episodes)
        policy.save(out_dir / f"{steps:07d}.npz")
        evaluations.append(statistics)
        attempts.extend(clips)
        write_json(out_dir / "evaluation.json", {"mission": args.mission, "trainingSeed": args.seed,
                                                  "testSeeds": TEST_SEEDS.tolist(), "checkpoints": evaluations})
        print(f"Evaluation {phase}: {statistics}", flush=True)

    with HarborBatch(args.mission, args.envs, args.host) as env:
        initial_seeds = rng.integers(1, 1_000_000_000, args.envs)
        initial = env.request("reset", initial_seeds)
        obs = initial["observations"]
        if args.trace is not None:
            trace = TrainingTrace(initial, initial_seeds)
        policy = Policy(obs.shape[1], rng)
        checkpoint(0)
        recent = Counter()
        for update in range(1, updates + 1):
            # Practice later slalom turns independently before switching to full-course starts.
            curriculum = args.mission == "slalom" and update <= updates * 0.70
            obs, batch, results = rollout(env, policy, obs, rng, args.horizon, curriculum, trace)
            policy.update(*batch, learning_rate=5e-4 * (1 - 0.75 * (update - 1) / updates))
            recent.update(results)
            if update == 1 or update % 10 == 0:
                print(f"Steps {update * stride:>7}: {dict(recent)}", flush=True)
                recent.clear()
            if update in checkpoint_updates:
                checkpoint(update)
    # All six pre-selected seeds are shown at each checkpoint, including failures.
    for i, attempt in enumerate(attempts, 1):
        attempt["id"] = i
    if args.replay is not None:
        write_json(args.replay, dict(mission=args.mission, source="ppo", trainingSeed=args.seed,
                                    evaluations=evaluations, attempts=attempts))
        print(f"Unity replay: {args.replay}", flush=True)
    if trace is not None:
        write_json(args.trace, dict(mission=args.mission, trainingSeed=args.seed,
                                   firstPass=trace.first_pass, episodes=trace.episodes,
                                   attempts=trace.attempts, evaluations=evaluations))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mission", choices=MISSIONS, default="current")
    parser.add_argument("--steps", type=int, default=160_000)
    parser.add_argument("--envs", type=int, default=24)
    parser.add_argument("--horizon", type=int, default=112)
    parser.add_argument("--seed", type=int, default=19)
    parser.add_argument("--host", type=Path, default=DEFAULT_HOST)
    parser.add_argument("--output", type=Path, default=ROOT / "Training/runs")
    parser.add_argument("--replay", type=Path, help="Write a JSON replay under Assets/Harbor/Resources/Replays.")
    parser.add_argument("--trace", type=Path, help="Record training episode counts and early trajectories.")
    args = parser.parse_args()
    if args.steps < 1 or not 1 <= args.envs <= 256 or args.horizon < 2:
        parser.error("Use positive steps, 1–256 environments and a horizon of at least 2.")
    train(args)


if __name__ == "__main__":
    main()
