"""Cut Motion Trials 02 from the recorded Harbor mission and its four PPO checkpoints."""

import argparse
from functools import lru_cache
import json
import math
from pathlib import Path

import numpy as np

from episode import Edit, FPS, duration, write_json
from network import Network


ROOT = Path(__file__).resolve().parent.parent
LIME = "B3F3D5"
CREAM = "FFF3D4"
INK = "10222B"


def full_frames(seconds):
    return math.ceil(seconds * FPS - 1e-6) / FPS


def at_frame(seconds):
    return round(seconds * FPS) / FPS


def shot_pose(run, gate):
    return next(frame["tick"] * .05 for frame in run["frames"] if frame["gateIndex"] >= gate)


def prepare(output, data, replay_file, width, height, stills):
    trace = json.loads((data / "trace.json").read_text(encoding="utf8"))
    replay = json.loads(replay_file.read_text(encoding="utf8"))
    if trace["mission"] != "harbor" or replay["mission"] != "harbor" or replay["source"] != "ppo":
        raise ValueError("Motion Trials 02 requires a recorded PPO Harbor mission.")
    runs = {run["id"]: run for run in trace["attempts"]}
    tests = {run["id"]: run for run in replay["attempts"]}
    records = trace["evaluations"]
    first = runs[trace["firstPass"]]
    failed = [runs[index] for index in range(1, first["id"])]
    assert first["outcome"] == 1 and first["frames"][-1]["gateIndex"] == 3
    assert all(run["outcome"] != 1 for run in failed)
    early, learning, middle, final = (tests[index] for index in (1, 7, 13, 19))
    assert len({run["seed"] for run in (early, learning, middle, final)}) == 1
    assert final["outcome"] == tests[20]["outcome"] == 1
    assert early["currentX"] < 0 < tests[20]["currentX"]

    @lru_cache(maxsize=None)
    def decisions(replay_id):
        run = tests[replay_id]
        actor = Network(data / f'{run["trainingSteps"]:07d}.npz')
        series = []
        for signal in run["signals"]:
            contributions = []
            for command in range(2):
                output_value, paths = actor.explain(signal["observation"], command)
                np.testing.assert_allclose(output_value, signal["action"], rtol=1e-4, atol=2e-5)
                for path in np.argsort(np.abs(paths), axis=None)[-3:][::-1]:
                    input_id, unit = np.unravel_index(path, paths.shape)
                    contributions.append(dict(input=int(input_id), unit=int(unit), output=command,
                                              value=float(paths[input_id, unit])))
            throttle, rudder = np.clip(signal["action"], -1, 1).tolist()
            series.append(dict(tick=signal["tick"], throttle=throttle, rudder=rudder, paths=contributions))
        return series

    edit = Edit()
    chapters = []

    def chapter(name):
        chapters.append((edit.clock, name))

    def cut(name, length, camera, run, *, display=None, montage_offset=0, **options):
        edit.shot(name, length, camera, run, **options)
        shot = edit.shots[-1]
        shot["display"] = display
        shot["montageOffset"] = montage_offset
        if run["phase"] != "training":
            shot["decisions"] = decisions(run["id"])
        return shot

    def caption(start, end, text):
        edit.say(start, end, text)

    def label(text, note=None, *, x=94, y=86, end=4.0):
        edit.box(x, y - 3, 44, 4, LIME, end=end)
        edit.label(x, y + 9, text, size=40, color=CREAM, end=end)
        if note:
            edit.label(x + 1, y + 65, note, size=25, bold=False, color=LIME, end=end)

    def fail(name, run, camera, lead, line, *, angle=0, sweep=0):
        end = duration(run)
        start = at_frame(max(0, end - lead))
        cut(name, full_frames(end - start + 1.30), camera, run, at=start, angle=angle, sweep=sweep)
        if line:
            caption(.22, min(end - start + .66, edit.length - .17), line)
        edit.cue(end - start, "impact" if run["outcome"] == 2 else "miss")

    def complete_run(prefix, run, windows):
        for index, (start, stop, camera) in enumerate(windows):
            cut(f"{prefix}-{index + 1}", stop - start, camera, run, at=start, sweep=13 if camera == "berth" else 0)
            if index == 0:
                label(f'ATTEMPT {run["id"]}' if run["phase"] == "training" else "FINAL MODEL",
                      "Continuous replay · real time", end=min(3, edit.length))
            if index == len(windows) - 1:
                finished = duration(run) - start
                edit.cue(finished, "pass")
                edit.label(1810, 918, "DOCKED", size=78, anchor=9, color=LIME, start=finished, end=edit.length)

    chapter("Three gates. One berth.")
    fail("01-cold-open", runs[600], "berth", 2.15, "Beacon. Brake.")
    cut("02-beacon", 3.50, "face", final, at=1.4, rate=.62, angle=14, sweep=-28)
    label("BEACON", "Motion Trials 02", end=3.5)
    cut("03-course", 6.50, "establish", final, rate=0, hide=True)
    caption(.28, 5.75, "Three buoy gates. One place to stop.")
    cut("04-gate-map", 6.5, "map", final, rate=0, hide=True, angle=-12, sweep=12)
    caption(.35, 5.9, "Go through every gate in order.")
    cut("05-berth-rule", 6, "berth", final, at=duration(final), rate=0, angle=17, sweep=-20)
    caption(.38, 5.75, "Finish inside the box, facing forward,\nbelow 0.38 metres per second.")

    chapter("Learning the gates")
    cut("06-attempt-one-board", 3.5, "score", runs[1], rate=0)
    cut("07-attempt-one", 7, "face", runs[1], at=4.2, sweep=26)
    caption(.52, 5.9, "A promising start, if the harbor is behind him.")
    edit.cue(duration(runs[1]) - 4.2, "impact")
    fail("08-gate-two-contact", runs[47], "gate", 6.1, "Gate one is clear. Gate two is less forgiving.", angle=1)
    cut("09-gate-two-route", 6, "map", runs[47], at=duration(runs[47]), rate=0, track=True)
    caption(.3, 5.5, "The red mark is where the run ended.")
    fail("10-gate-three-contact", runs[120], "gate", 6.3, "One more turn. He starts it a little late.", angle=2)
    fail("11-first-dock-contact", runs[125], "berth", 7.2, "All the gates. Still too far left or right,\nand the berth will catch him.")
    cut("12-attempt-186-bow", 6.25, "onboard", runs[186], at=24.85)
    caption(.4, 5.9, "From the bow, this looks almost wide enough.")
    edit.cue(duration(runs[186]) - 24.85, "impact")
    cut("13-approach-599", 8, "berth", runs[599], at=duration(runs[599]) - 6.65, sweep=19)
    caption(.65, 5.23, "He gets farther in, but can't hold the centreline.")
    edit.cue(6.65, "impact")
    cut("14-training-method", 5.5, "waterline", runs[598], at=6.7, angle=-20, sweep=50)
    caption(.3, 5.2, "The trainer runs 24 boats in parallel.\nEach one tries its own controls.")

    chapter("603 failed attempts at 400×")
    montage = full_frames(sum(duration(run) for run in failed) / 400)
    montage_start = edit.clock
    offset = 0
    for index, (length, camera, angle, sweep) in enumerate(((8, "plan", -6, 6), (10.5, "map", 7, -11),
                                                           (montage - 18.5 + 1, "plan", 2, 0))):
        cut(f"15-training-routes-{index + 1}", length, camera, first, rate=0, angle=angle, sweep=sweep,
            display="montage", hide=True, montage=failed, montage_offset=offset)
        if index == 0:
            caption(.4, 5.1, "Each path is a completed training run.\nWe're playing their combined time at 400×.")
            edit.cue(0, "accelerate")
        elif index == 1:
            caption(.75, 5.7, "The routes start reaching the far side.")
        else:
            caption(4.35, min(9.2, length - .2), "Plenty of runs clear the gates.\nThe boat still has to stop.")
            edit.cue(length - 1, "brake")
        offset += length
    montage_end = edit.clock

    chapter(f'Attempt {first["id"]}: the first docking')
    cut("16-last-miss", 6, "berth", runs[first["id"] - 1], at=duration(runs[first["id"] - 1]) - 5, angle=-10, sweep=-22)
    caption(.25, 4.3, "That one missed the berth and kept going.")
    edit.cue(5, "impact")
    cut("17-first-docking-score", 3.6 + 1/15, "score", first, rate=0)
    first_begin = edit.clock
    split_a = at_frame(shot_pose(first, 1) + .74)
    split_b = at_frame(shot_pose(first, 3) + .64)
    complete_run("18-first-docking", first, ((0, split_a, "follow"), (split_a, split_b, "follow"),
                                            (split_b, full_frames(duration(first) + 2.1), "berth")))
    first_end = first_begin + duration(first)
    caption(max(.33, duration(first) - split_b - 4.5), duration(first) - split_b - .20,
            "The box is almost behind him.")

    chapter("Inside the policy")
    cut("19-before-training", 6, "plan", early, at=9.0, rate=1, angle=6)
    label("FIXED TEST 01", "Before training", end=4.6)
    caption(.95, 5.65, "For testing, I saved the same 48 starts.\nHere's one before any training.")
    cut("20-policy-before", 4.8 + 1/30, "monitor", early, at=0, rate=.84)
    caption(.26, 4.55, "The throttle barely moves. The current does the rest.")
    edit.cue(.19, "insight")
    cut("21-same-start-trained", 7.25, "waterline", final, at=0, angle=14, sweep=18)
    label("FIXED TEST 01", "Final checkpoint", end=4.6)
    caption(.74, 5.8, "Same start. Same current. Now look at that first turn.")
    cut("22-policy-after", 4.8 + 1/30, "monitor", final, at=0, rate=.84)
    caption(.25, 4.55, "The strongest paths light up. Teal adds. Amber subtracts.")
    edit.cue(.19, "insight")
    cut("23-what-the-boat-gets", 7.75, "onboard", learning, at=5.5, rate=.91)
    label("BOW CAMERA", end=3.4)
    caption(.4, 3.7, "We get the view from the bow.")
    caption(3.9, 7.5, "Beacon gets 13 numbers, including\nwhere the next gate is and how fast he's moving.")
    cut("24-the-brakes", 7, "berth", learning, at=12.1, rate=1, angle=-14, sweep=29)
    caption(.58, 5.23, "Once the gates are clear, the target changes to the berth.")
    cut("25-slowing-earlier", 7.5, "waterline", middle, at=10.2, rate=.89, angle=20, sweep=-21)
    caption(1.12, 5.7, "Watch the wake shrink as he eases off.")

    chapter("The cross-current changes sides")
    cut("26-current-left", 7.5, "map", final, at=5.05, track=False, angle=-7, sweep=22)
    label("TEST 01", f'{abs(final["currentX"]):.2f} m/s current to port', end=5.6)
    caption(.43, 5.7, "The current is pushing left on this run.")
    other = tests[20]
    cut("27-current-right", 7.5, "follow", other, at=4.8, angle=16, sweep=-44)
    label("TEST 02", f'{other["currentX"]:.2f} m/s current to starboard', end=5.6)
    caption(.43, 5.5, "New starting position. Current the other way.")
    cut("28-test-two-bow", 6.25, "onboard", other, at=10.3, rate=1)
    label("BOW CAMERA", end=3.7)
    caption(1.03, 5.4, "Straighten up. Now let the speed come down.")
    edit.cue(duration(other) - 10.3, "pass_soft")
    cut("29-two-routes", 7.75, "plan", final, at=duration(final), rate=0, track=True, alternative=other, angle=8, sweep=-14)
    caption(.42, 6.2, "Two starts, two different lines into the same berth.")

    chapter("Learning to slow down")
    brake_test = tests[24]
    cut("29a-speed-at-gate", 5.75, "gate", brake_test, at=7.6, rate=.79, angle=2)
    caption(.25, 5.32, "The final gate is still a speed run.\nThe berth is where that approach stops working.")
    cut("29b-brake-and-straighten", 6.75, "waterline", brake_test, at=10.5, rate=.72, angle=16, sweep=-19)
    caption(.39, 5.95, "Ease off early enough to straighten up\nbefore the bow reaches the box.")
    shot = cut("29c-hold-the-berth", 7.25, "berth", brake_test, at=13.2, rate=.55, angle=9, sweep=-18)
    speeds = brake_test["frames"]
    for i in range(math.ceil(edit.length / .2)):
        start, stop = i * .2, min(edit.length, (i + 1) * .2)
        speed = np.interp((shot["offset"] + start * shot["rate"]) / .05,
                          [frame["tick"] for frame in speeds], [frame["speed"] for frame in speeds])
        edit.label(110, 171, f"{speed:.2f} m/s", size=51, color=LIME if speed < .38 else CREAM,
                   start=start, end=stop, fade=0)
    label("SPEED OVER GROUND", end=7.25)
    caption(.52, 5.92, "Position, heading, and speed must all hold\nfor half a second.")

    chapter("The full final run")
    final_begin = edit.clock
    split_a = at_frame(shot_pose(final, 1) + .58)
    split_b = at_frame(shot_pose(final, 3) + .69)
    complete_run("30-final-replay", final, ((0, split_a, "follow"), (split_a, split_b, "map"),
                                           (split_b, full_frames(duration(final) + 2.75), "berth")))
    final_end = final_begin + duration(final)

    chapter("48 fixed starts")
    cut("31-checkpoints", 11.25, "establish", final, at=duration(final), rate=0)
    edit.box(99, 110, 713, 554, INK, alpha="12")
    edit.label(131, 132, "48 FIXED TEST STARTS", size=51, color=CREAM)
    edit.label(134, 213, "Same starts at every checkpoint", size=28, color=LIME, bold=False)
    for index, checkpoint in enumerate(records):
        y = 295 + index * 80
        start = .32 + index * .68
        count = checkpoint["completedTrainingEpisodes"]
        passes = checkpoint.get("pass", 0)
        edit.box(134, y - 11, 643, 1, "628085", start=start)
        edit.label(135, y, f"{count:,} completed training runs", size=28, color=CREAM, start=start, bold=False)
        edit.label(772, y - 3, f"{passes} / 48", size=34, anchor=9, color=LIME if passes == 48 else CREAM, start=start)
    caption(6.1, 10.3, "At the final checkpoint, all 48 starts get in cleanly.")
    cut("32-second-test-finish", 7.75, "berth", tests[21], at=11.2, angle=12, sweep=23)
    caption(1, 5.3, "The first docking took 604 attempts.\nDoing it reliably took a little longer.")
    cut("33-quiet-berth", 6.5, "face", final, at=duration(final), rate=0, angle=21, sweep=-44)
    caption(.45, 4.7, "Good parking, Beacon.")

    chapter("Code and replay data")
    cut("34-boat-lab", 8, "berth", final, at=11.7, rate=.77, angle=-18, sweep=26)
    edit.box(0, 0, 1920, 1080, INK, alpha="85")
    edit.label(102, 280, "BOAT LAB", size=88, color=CREAM)
    edit.label(107, 389, "Chart What’s Next", size=47, color=LIME)
    edit.label(108, 531, "Unity project, PPO code and replays on GitHub", size=35, color=CREAM, bold=False)
    edit.label(110, 594, "Open the GitHub link on the Boat Lab channel", size=26, color=LIME, bold=False)
    edit.label(111, 784, "Partnerships  ·  boatlabstudio@gmail.com", size=27, color=CREAM, bold=False)
    edit.cue(1.1, "end")

    output.mkdir(parents=True, exist_ok=True)
    if stills:
        cover = dict(name="cover", camera="cover", attempt=other, length=1, offset=12.40, rate=0,
                     decisions=decisions(other["id"]))
        dock_cover = dict(name="cover-dock", camera="cover-dock", attempt=first, length=1, offset=20.40, rate=0)
        map_cover = dict(name="cover-map", camera="cover-map", attempt=first, length=1, offset=19.2, rate=0)
        fleet_cover = dict(name="cover-fleet", camera="cover-fleet", attempt=first, length=1, offset=20.40, rate=0)
        shots = [cover, dock_cover, map_cover, fleet_cover, *[shot for shot in edit.shots if shot["name"] in
                 {"01-cold-open", "02-beacon", "03-course", "04-gate-map", "06-attempt-one-board",
                  "15-training-routes-3", "20-policy-before", "22-policy-after", "30-final-replay-3", "34-boat-lab"}]]
        write_json(output / "shots.json", dict(width=width, height=height, fps=FPS, mission="harbor", shots=shots))
        return

    assert edit.clock >= 300, edit.clock
    edit.save(output, width, height, mission="harbor", subtitle_name="Beacon-02.en.srt")
    sound = dict(duration=edit.clock, cues=edit.sound, music=music_bed(edit, montage_start, montage_end,
                                                                    first_end, final_begin, final_end))
    write_json(output / "sound.json", sound)
    write_json(output / "chapters.json", chapters)
    write_json(output / "milestones.json", {"firstDockingAttempt": first["id"], "firstDockingSeconds": duration(first),
                                           "finalSeconds": duration(final), "finalTrainingEpisodes": records[-1]["completedTrainingEpisodes"],
                                           "evaluations": records, "duration": edit.clock})
    print(f"Motion Trials 02: {edit.clock:.2f}s, first docking at attempt {first['id']}")


def music_bed(edit, montage_start, montage_end, first_end, final_begin, final_end):
    score = []

    def place(track, start, end, source, gain=.83, fade_in=.45, fade_out=.85, tempo=False):
        score.append(dict(track=track, start=start, end=end, source=source, gain=gain,
                          fade_in=fade_in, fade_out=fade_out, tempo=tempo))

    practice, theme = "special-ops", "tomorrow"
    place(theme, 3.0, 25.3, 7.5, .76)
    place(practice, 25.15, 64.1, 21.0, .76, .7, 1.55)
    place(practice, 63.0, montage_start - .15, 91, .72, 1.4, .5)
    place(practice, montage_start, montage_end - .31, 118.0, .87, .3, .63, tempo=True)
    place(practice, montage_end + .15, first_end - 2.9, 53.5, .67, .8, 3.0)
    place(theme, first_end - .15, first_end + 18.4, 74, .77, .23, 2.2)
    place(practice, first_end + 17.2, final_begin - 1.8, 86.2, .60, 1.0, 3.2)
    place(theme, final_begin, final_end + 9.3, 38.0, .68, 1.3, 2.1)
    place(theme, final_end + 8.1, edit.clock, 98.0, .77, 1.6, 3.2)
    return score


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--data", type=Path, default=ROOT / "Film/episode-02")
    parser.add_argument("--replay", type=Path, default=ROOT / "Assets/Harbor/Resources/Replays/harbor.json")
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--height", type=int, default=1080)
    parser.add_argument("--stills", action="store_true")
    args = parser.parse_args()
    prepare(args.output, args.data, args.replay, args.width, args.height, args.stills)
