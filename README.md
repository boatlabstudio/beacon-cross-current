# Beacon 01 · Harbor Learning

A Unity simulation and film series following a survey catamaran learning to navigate a harbor. Episode 01, **AI Learns to Drive a Boat**, follows Beacon through a six-metre buoy gate in a lateral current.

| Episode 01 milestone | Completed training episodes | Fixed evaluation |
| --- | ---: | ---: |
| Before training | 0 | 0 / 48 |
| First successful training crossing | 170 | |
| First intermediate checkpoint | 444 | 26 / 48 |
| First recorded checkpoint with a perfect score | 1,278 | 48 / 48 |
| Final checkpoint | 3,320 | 48 / 48 |

The film shows the 169 recorded routes before the first successful training crossing, the entire first success, a missed gate, and a matched comparison of a buoy strike and a clean pass. The featured crossing takes 8.80 seconds. The bow camera is a replay view. The actor receives 13 numeric observations from the simulator and outputs two commands through one 48-unit tanh layer. The inset graphs show the largest signed contributions to the steering command, recalculated from each checkpoint and the recorded observations. The path scores use a zero-input baseline and integrate the tanh response between the baseline and the recorded input.

## Run the pilot

Open this folder in Unity **6.3**. Open `Assets/Harbor/Scenes/HarborPilot.unity` and press **Play**. The scene starts with the cross-current trial. All four missions include recorded PPO replays. You can watch them without Python.

| Control | Action |
| --- | --- |
| 1–4 | Gate, cross-current, slalom, or berth |
| Space / N / R | Play or pause, next attempt, restart |
| Tab / F | Cycle auto, overview, chase, and onboard cameras, or change montage speed |
| T / H | Show routes, switch film mode |
| M | Manual control with WASD or arrow keys |

Orange routes missed a gate, red routes hit a buoy or basin boundary, amber routes reached the time limit, and green routes passed. During the montage, the last part of a run plays at normal speed. The film mode hides playback controls and keeps the score and captions.

## Reproduce the training

Requirements: the **.NET 10 SDK**, **Python 3.10–3.13**, and NumPy. Unity can remain closed during training. Run these commands from this folder:

```sh
dotnet build Training/HarborHost -c Release
python -m pip install -r Training/requirements.txt
python Training/train.py --mission current --steps 180000 --seed 19 --replay Assets/Harbor/Resources/Replays/current.json
```

Use `--mission gate --steps 90000`, `--mission slalom --steps 480000`, or `--mission dock --steps 500000` with the corresponding output filename to regenerate the other reels. Steps count policy decisions across the batch. Each decision holds two continuous commands (throttle and rudder) for up to four 50 ms physics ticks.

The simulation is shared between Unity and the local training process. The training script samples experience, computes generalized advantage estimates, and updates a Gaussian actor and a value network with PPO-Clip. It records 48 fixed, training-excluded test starts at each checkpoint. Each film reel contains the first six of those same starts per checkpoint, including failures. Slalom practice starts at several gates early in training and switches to full-course starts. Every evaluation starts at the beginning of its course.

| Code | Purpose |
| --- | --- |
| `Assets/Harbor/Simulation/HarborEnvironment.cs` | Vessel dynamics, scenarios, observations, rewards, and outcomes |
| `Training/HarborHost/` | Batched local simulator interface over standard input and output |
| `Training/ppo.py` | NumPy policy, value network, optimizer, and advantages |
| `Training/train.py` | Checkpoints, fixed-seed evaluations, and replay export |
| `Assets/Harbor/Presentation/` | Catamaran and basin, replay, cameras, routes, and captions |

Checkpoints and detailed metrics are written to `Training/runs/`. The tracked replay JSON also contains checkpoint evaluation counts and trajectory frames. A scenario without a PPO recording can use the labelled controller demonstration. Regenerate those demonstrations with:

```sh
dotnet Training/HarborHost/bin/Release/net10.0/HarborHost.dll --demo Assets/Harbor/Resources/Demonstrations
```

The pilot uses a simplified planar vessel model for simulated experiments. Its success rates describe these simulated courses and seeds.

PPO reference: [Schulman et al., Proximal Policy Optimization Algorithms](https://arxiv.org/abs/1707.06347).

## Rebuild Episode 01

The archived experiment in `Film/episode-01/` contains the ordered training trace, the fixed-start evaluation trajectories and telemetry, and the three policy checkpoints used in the network graphics. Unity captures the camera shots at 1920 × 1080 and 24 fps. Python builds the English captions and mixes the score with synthesized water, motor and impact effects. FFmpeg composites the captions and encodes the MP4.

On Windows, install the training dependencies above and add FFmpeg with libass and libx264 to PATH. Point the render script at your licensed Unity 6.3 editor:

```powershell
./Film/render.ps1 -Unity 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe'
```

The script writes the MP4, English SRT, and thumbnail to `Preview/Beacon01-Recut/`. The default render uses the original synthesized score.

To reproduce the revised music mix, download Scott Buckley's [Special Ops](https://www.scottbuckley.com.au/library/special-ops/) as `special-ops.mp3` and [Tomorrow](https://www.scottbuckley.com.au/library/tomorrow/) as `tomorrow.mp3`, put them in one folder, and pass its path as `-MusicDirectory`. The two tracks are licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The mix uses edited excerpts and speeds up the montage cue. Use the music credit in `Film/YouTube-description.txt` for the revised mix.

To regenerate the episode data from the initial weights:

```sh
python Training/train.py --mission current --steps 180000 --seed 19 --replay Training/runs/replay.json --trace Training/runs/trace.json
```

Pass `-Data Training/runs` to the render script to use those results. The edit targets this run's milestones and trajectory IDs.

The trace numbers finished runs by decision batch, then by terminal physics tick within the batch and environment index. The 400× sequence uses the sum of individual episode durations. The final crossing is timed to the finish event, followed by 1.4 seconds of recorded coasting with both controls released so the stern clears the gate.
