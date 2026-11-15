"""Assemble the episode edit from the actual PPO trace and checkpoint replay."""

import argparse
from pathlib import Path
import json
import math

import numpy as np
from network import Network


FPS = 24
MINT = "8EE3B2"
WHITE = "F4F2E9"
DARK = "101D22"
PAPER = "F5F1E6"
INK = "20292B"
PENCIL = "52615C"
PASS_INK = "22654F"
FAIL_INK = "A14331"


def duration(attempt):
    return attempt["frames"][-1]["tick"] * .05


def ass_time(seconds):
    centiseconds = round(seconds * 100)
    minutes, value = divmod(centiseconds, 6000)
    hours, minutes = divmod(minutes, 60)
    return f"{hours}:{minutes:02d}:{value // 100:02d}.{value % 100:02d}"


def srt_time(seconds):
    milliseconds = round(seconds * 1000)
    minutes, value = divmod(milliseconds, 60000)
    hours, minutes = divmod(minutes, 60)
    return f"{hours:02d}:{minutes:02d}:{value // 1000:02d},{value % 1000:03d}"


def bgr(color):
    return color[4:] + color[2:4] + color[:2]


class Edit:
    def __init__(self):
        self.clock = 0
        self.shots = []
        self.ass = []
        self.subtitles = []
        self.sound = []

    def shot(self, name, length, camera, attempt, *, at=0, rate=1, angle=0,
             sweep=0, track=False, alternative=None, hide=False, montage=None):
        self.start = self.clock
        self.clock += length
        self.length = length
        self.shots.append(dict(name=name, camera=camera, attempt=attempt, length=length,
                               offset=at, rate=rate, angle=angle, sweep=sweep, track=track,
                               compare=alternative is not None, alternative=alternative,
                               hideBoat=hide, montage=montage))
        return self

    def event(self, layer, start, end, style, value):
        times = f"{ass_time(self.start + start)},{ass_time(self.start + end)}"
        self.ass.append(f"Dialogue: {layer},{times},{style},,0,0,0,,{value}")

    def label(self, x, y, value, *, size=28, color=WHITE, start=0, end=None,
              layer=4, anchor=7, font="Segoe UI", bold=True, fade=140):
        tags = (rf"\an{anchor}\pos({x},{y})\fn{font}\fs{size}"
                rf"\1c&H{bgr(color)}&\b{int(bold)}\fad({fade},{fade})")
        self.event(layer, start, self.length if end is None else end, "Text",
                   "{" + tags + "}" + value.replace("\n", r"\N"))

    def shape(self, path, color, *, alpha="00", start=0, end=None, layer=1):
        tags = rf"\an7\pos(0,0)\bord0\shad0\p1\1c&H{bgr(color)}&\1a&H{alpha}&"
        self.event(layer, start, self.length if end is None else end, "Vector",
                   "{" + tags + "}" + path + r"{\p0}")

    def box(self, x, y, w, h, color=DARK, **options):
        self.shape(f"m {x} {y} l {x+w} {y} {x+w} {y+h} {x} {y+h} {x} {y}", color, **options)

    def lower_third(self, title, detail=None, end=4.2):
        start = .18
        self.box(55, 796, 416, 88 if detail else 60, PAPER, start=start, end=end, layer=3)
        self.label(73, 802, title, size=30, color=INK, font="Georgia", start=start, end=end, fade=100)
        if detail:
            self.label(74, 848, detail, size=20, color=PENCIL, bold=False, start=start, end=end, fade=100)

    def say(self, start, end, value):
        assert 0 <= start < end <= self.length, (self.shots[-1]["name"], start, end)
        self.subtitles.append((self.start + start, self.start + end, value))
        self.event(8, start, end, "Caption", value.replace("\n", r"\N"))

    def stamp(self, time, heading, note, color=PASS_INK):
        finish = self.length
        self.box(55, 813, 362, 88, PAPER, start=time, end=finish, layer=3)
        self.label(73, 819, heading, size=32, color=color, font="Georgia", start=time, end=finish, fade=90)
        self.label(75, 866, note, size=19, color=PENCIL, bold=False, start=time, end=finish, fade=90)

    def cue(self, local, kind):
        self.sound.append(dict(time=self.start + local, kind=kind))

    def telemetry(self, attempt, offset=0, rate=1):
        self.box(55, 682, 406, 201, PAPER)
        self.label(74, 693, "From the simulator", size=30, color=INK, font="Georgia")
        self.box(75, 739, 362, 1, PENCIL)
        for seconds, signal in live_signals(attempt, offset, rate, self.length):
            start, end = seconds
            dx, dz = signal["observation"][:2]
            cx = signal["observation"][8]
            throttle, rudder = np.clip(signal["action"], -1, 1)
            self.label(75, 752, f"Gate X / Z   {dx*18:+.1f} / {dz*36:+.1f} m",
                       size=21, color=INK, start=start, end=end, fade=0, bold=False)
            self.label(75, 792, f"Side current   {cx:+.2f} m/s",
                       size=21, color=INK, start=start, end=end, fade=0, bold=False)
            self.label(75, 832, f"Throttle {throttle:+.2f}    Rudder {rudder:+.2f}",
                       color=INK, size=21, start=start, end=end, fade=0)

    def save(self, directory, width, height, *, mission=None, subtitle_name="Beacon-01.en.srt"):
        header = """[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080
ScaledBorderAndShadow: yes
YCbCr Matrix: TV.709

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Text,Segoe UI,24,&H00E9F2F4,&H00FFFFFF,&H350E161B,&H800E161B,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
Style: Caption,Segoe UI Semibold,45,&H00F8F7F5,&H00FFFFFF,&H1610120E,&H6010120E,0,0,0,0,100,100,0,0,1,2.4,1.5,2,190,190,56,1
Style: Vector,Arial,24,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
        (directory / "episode.ass").write_text(header + "\n".join(self.ass) + "\n", encoding="utf-8")
        srt = "\n".join(f"{i}\n{srt_time(a)} --> {srt_time(b)}\n{line}\n"
                        for i, (a, b, line) in enumerate(self.subtitles, 1))
        (directory / subtitle_name).write_text(srt, encoding="utf-8")
        film = dict(width=width, height=height, fps=FPS, shots=self.shots)
        if mission:
            film["mission"] = mission
        write_json(directory / "shots.json", film)
        write_json(directory / "sound.json", dict(duration=self.clock, cues=self.sound))


def write_json(path, data):
    path.write_text(json.dumps(data, separators=(",", ":")) + "\n", encoding="utf-8")


def live_signals(attempt, offset, rate, length, interval=.2):
    # The terminal frame has no subsequent policy action. Hold the last action in the tail.
    signals = attempt["signals"]
    for i in range(math.ceil(length / interval)):
        start = i * interval
        tick = (offset + start * rate) / .05
        index = min(len(signals) - 1, max(0, int(tick / 4 + 1e-6)))
        yield (start, min(start + interval, length)), signals[index]


def score_panel(edit, evaluations):
    edit.box(55, 555, 475, 156 + 43 * len(evaluations), PAPER)
    edit.label(75, 568, "48 test starts", size=34, color=INK, font="Georgia")
    edit.label(76, 621, "Same starts at every checkpoint", size=21, color=PENCIL, bold=False)
    edit.box(76, 668, 431, 1, PENCIL)
    for i, record in enumerate(evaluations):
        count = record["completedTrainingEpisodes"]
        passes = record.get("pass", 0)
        y = 678 + 43 * i
        edit.label(76, y, f"{count:,} training runs", size=23, color=INK, bold=False)
        edit.label(505, y, f"{passes} / 48", anchor=9, size=24, color=PASS_INK if passes == 48 else INK)


def assemble(directory):
    trace = json.loads((directory / "trace.json").read_text(encoding="utf-8"))
    replay = json.loads((directory / "replay.json").read_text(encoding="utf-8"))
    records = trace["evaluations"]
    runs = {attempt["id"]: attempt for attempt in trace["attempts"]}
    tests = {attempt["id"]: attempt for attempt in replay["attempts"]}
    first = runs[trace["firstPass"]]
    failed = [runs[i] for i in range(1, first["id"])]
    early, learning, middle, final = (tests[i] for i in (2, 8, 14, 20))
    assert len({clip["seed"] for clip in (early, learning, middle, final)}) == 1
    assert [clip["outcome"] for clip in (early, learning, middle, final)] == [2, 2, 1, 1]
    full = next(record for record in records if record.get("pass") == record["episodes"])
    finish = duration(final)
    final_with_coast = dict(final, frames=final["frames"] + final["coastFrames"])

    def network(attempt):
        filename = f'{attempt["trainingSteps"]:07d}.npz'
        checkpoint = next(path for path in (directory / filename, directory / "current" / filename,
                                             directory / "runs/current" / filename) if path.is_file())
        return Network(checkpoint)

    edit = Edit()
    edit.shot("01-cold-open", 8, "onboard", learning, at=4.2)
    edit.say(.7, 3.3, "He's lined up. I think.")
    edit.say(4.15, 5.8, "Left buoy. Left buoy.")
    edit.stamp(duration(learning) - 4.2, "Clipped it", "Buoy contact", FAIL_INK)
    edit.cue(duration(learning) - 4.2, "impact")

    edit.shot("02-title", 9, "detail", final, at=1.4, rate=.72, angle=-62, sweep=118)
    edit.box(0, 0, 1920, 1080, DARK, alpha="AA")
    edit.box(108, 268, 64, 4, MINT)
    edit.label(109, 286, "BEACON     EPISODE 01", size=24, color=MINT)
    edit.label(100, 350, "AI Learns to\nDrive a Boat", size=110, font="Bahnschrift")
    edit.label(110, 621, "The cross-current trial", size=34, bold=False)
    edit.cue(0, "title")

    edit.shot("03-the-boat", 7, "detail", final, rate=.30, angle=100, sweep=-78)
    edit.say(.8, 5.9, "Meet Beacon. I built him a harbor to practice in.")

    edit.shot("04-the-gate", 8, "gate", final, at=3.2, rate=0, angle=1, sweep=25)
    edit.say(.5, 4, "The job is to go between those two buoys.")
    edit.say(4.25, 7.8, "The water is pushing him sideways the whole time.")

    edit.shot("05-first-launch", 8, "chase", early, angle=-24, sweep=33)
    edit.lower_third("Before training", "Test start 02")
    edit.say(.6, 5.7, "At the start, the weights are random.\nLet's see where that gets us.")

    edit.shot("06-first-drift", 7, "approach", early, at=11.5, angle=-76, sweep=13)
    edit.say(.55, 4.7, "He's making excellent time toward the wrong dock.")
    edit.stamp(duration(early) - 11.5, "Hit the wall", "Test start 02", FAIL_INK)
    edit.cue(duration(early) - 11.5, "impact")

    edit.shot("07-first-track", 6, "plan", early, at=duration(early), rate=0, track=True, angle=0)
    edit.say(.7, 5.6, "There's the gate, all the way up top.\nSave this route. We'll see it again.")

    edit.shot("08-practice", 11, "wide", runs[42], at=3.8, rate=1, angle=17, sweep=-29)
    edit.lower_third("Training run 42")
    edit.say(.5, 5, "I gave him throttle and rudder. He tried both.")
    edit.say(5.6, 10.6, "PPO nudges the controls toward whatever earned points.")

    miss = runs[163]
    edit.shot("09-missed-gate", 8, "gate", miss, at=duration(miss)-6.5, angle=-37, sweep=16)
    edit.lower_third("Training run 163")
    edit.say(.55, 5.9, "That's the finish line, Beacon.\nThe gap is a little to your left.")
    edit.stamp(6.5, "Outside the gate", "Crossed wide on the right", FAIL_INK)
    edit.cue(6.5, "miss")

    edit.shot("10-reward", 13, "approach", learning, at=2, rate=.65, angle=12, sweep=-28)
    edit.box(55, 735, 314, 123, PAPER, start=1, end=10)
    edit.label(75, 754, "+2.00   Clean pass", size=26, color=PASS_INK, start=1, end=10)
    edit.label(75, 804, "−1.20   Collision", size=26, color=FAIL_INK, start=1, end=10)
    edit.say(.8, 6.25, "Getting closer is worth a little.\nGetting through cleanly is worth a lot.")
    edit.say(7.4, 12.5, "Touch a buoy and the run ends right there.")
    edit.cue((duration(learning) - 2) / .65, "impact")

    edit.shot("11-bow-telemetry", 14, "onboard", final, rate=.61)
    edit.telemetry(final, rate=.61)
    edit.say(.7, 5.8, "I put a camera on the bow so we can see the gap.")
    edit.say(6.15, 11.7, "Beacon gets the gate position, heading and current\nas numbers from the simulator.")

    edit.shot("12-neural-network", 8, "plan", early, rate=0)
    network(early).draw(edit, early, title="Beacon's neural network", command=0, start=0, end=8, fixed=True)
    edit.say(.65, 4.25, "Here's what those numbers feed into.")
    edit.say(4.55, 7.8, "At launch, the untrained network barely opens the throttle.")
    edit.cue(0, "insight")

    edit.shot("13-batch-setup", 6, "detail", runs[91], at=6.4, angle=-53, sweep=72)
    edit.say(.6, 5.65, "I ran 24 boats in parallel.\nHere's where the first 169 ended up.")

    montage_length = math.ceil(sum(map(duration, failed)) / 400 * FPS) / FPS
    edit.shot("14-training-400x", montage_length + 2, "plan", final, rate=0, hide=True, montage=failed)
    edit.box(1510, 68, 350, 143, PAPER)
    edit.label(1528, 78, "Training at 400×", size=32, color=INK, font="Georgia")
    edit.label(1529, 126, "Total time across 24 boats", size=19, color=PENCIL, bold=False)
    time = 0
    for i, attempt in enumerate(failed):
        start = time
        time += duration(attempt) / 400
        edit.label(1528, 153, f"{i:03d} / {len(failed)}", size=36, color=INK,
                   start=start, end=time, fade=0)
    edit.label(1528, 153, f"{len(failed):03d} / {len(failed)}", size=36, color=INK,
               start=time, fade=0)
    edit.say(1.1, 5.6, "Every line is a run. Every cross is where it ended.")
    edit.cue(0, "accelerate")
    edit.cue(montage_length, "brake")

    nearly = runs[first["id"] - 1]
    edit.shot("15-last-failure", 8, "wide", nearly, at=duration(nearly)-7.2, angle=24, sweep=-18)
    edit.lower_third(f"Training run {nearly['id']}")
    edit.say(.65, 5.5, "One run before the breakthrough.\nStill getting dragged across the basin.")
    edit.stamp(7.2, "Out of bounds", "The streak reaches 169", FAIL_INK)
    edit.cue(7.2, "impact")

    edit.shot("16-first-clean-pass", math.ceil(duration(first)) + 2, "approach", first, angle=-22, sweep=29)
    edit.lower_third(f"Training run {first['id']}", "Real time")
    edit.say(.8, 5.1, "Next run. Same gate.")
    edit.say(8.4, 12.5, "That turn looks way too early.")
    edit.say(16.7, 20.1, "Wait. He's bringing it back.")
    edit.say(20.5, 22.9, "Hold it…")
    edit.stamp(duration(first), "First clean pass", f"{duration(first):.2f} seconds")
    edit.cue(duration(first), "pass")

    edit.shot("17-first-pass-route", 6, "plan", first, at=duration(first), rate=0, track=True)
    edit.say(.55, 5.6, "One lucky run? Time to bring back the test starts.")

    edit.shot("18-test-starts", 8, "wide", middle, at=.4, rate=.63, angle=-19, sweep=33)
    score_panel(edit, records[:1])
    edit.say(.6, 5.8, "I kept 48 starts out of training and ran the same test\non each saved version of Beacon.")

    edit.shot("19-test-midpoint", 8, "chase", tests[7], at=2.6, rate=.87, angle=-28, sweep=36)
    score_panel(edit, records[:2])
    edit.say(.7, 5.8, "Twenty-six went through. This was one of them.")

    edit.shot("20-test-perfect", 8, "gate", middle, at=3.15, rate=.80, angle=21, sweep=-20)
    score_panel(edit, records[:3])
    edit.say(.7, 5.75, "This is the first saved version that got all 48.")
    edit.cue(6.8, "pass_soft")

    edit.shot("21-mid-training-decision", 12, "approach", learning, angle=6, sweep=-20)
    network(learning).draw(edit, learning, title="Mid-training", command=1, start=.5, end=9.75, inset=True)
    edit.say(.65, 4.75, "Back to the opening crash. Watch the rudder.")
    edit.say(5.25, 9.15, "The current is still moving him left.\nHe runs out of room.")
    edit.stamp(duration(learning), "Buoy contact", "Left edge of the gate", FAIL_INK)
    edit.cue(duration(learning), "impact")

    edit.shot("22-route-comparison", 10, "plan", final, at=finish, rate=0, track=True, alternative=learning)
    edit.box(55, 779, 286, 104, PAPER)
    edit.label(74, 792, "Mid-training", size=25, color=FAIL_INK)
    edit.label(74, 837, "Final model", size=25, color=PASS_INK)
    edit.say(.8, 5.6, "The red route slides into the buoy.")
    edit.say(5.95, 9.6, "The green one gives it room.")

    edit.shot("23-final-decision", 12, "approach", final, rate=.73, angle=-32, sweep=18)
    network(final).draw(edit, final, title="Final model", command=1, start=.5, end=10.75, inset=True)
    edit.say(.7, 5.5, "Now watch the first correction.\nThe network changes the rudder much sooner.")
    edit.say(6.05, 11.25, "The lit links show what is pushing the command\nup or down at that moment.")
    edit.cue(.55, "insight")

    edit.shot("24-final-exterior", 11, "chase", final_with_coast, angle=-15, sweep=25)
    edit.lower_third("The full pass", "Real time", end=2.9)
    edit.stamp(finish, "Clear", f"{finish:.2f} seconds")
    edit.cue(finish, "pass")

    edit.shot("25-final-bow", 11, "onboard", final_with_coast)
    edit.lower_third("Bow camera", "The same pass in real time", end=2.9)
    edit.stamp(finish, "Through", "Throttle released")
    edit.cue(finish, "pass_soft")

    edit.shot("26-gate-slow", 9, "gate", final_with_coast, at=6.0, rate=.48, angle=9, sweep=26)
    edit.say(.65, 5.9, "Watch the bow. He's pointing into the current\nwhile the boat travels down the middle.")

    opposite = tests[19]
    edit.shot("27-opposite-current", 10, "chase", dict(opposite, frames=opposite["frames"] + opposite["coastFrames"]),
              angle=30, sweep=-29)
    edit.lower_third("Current pushing right", "+0.59 m/s · another test start")
    edit.say(.7, 5.1, "Turn the current around and the bow leans the other way.")
    edit.stamp(duration(opposite), "Clear again", "The same final model")
    edit.cue(duration(opposite), "pass_soft")

    for test_id, angle in ((22, -27), (23, 28), (24, 7)):
        trial = tests[test_id]
        seconds = duration(trial)
        edit.shot(f"28-test-{test_id}", 4, "gate", dict(trial, frames=trial["frames"] + trial["coastFrames"]),
                  at=seconds - 2.6, angle=angle, sweep=13)
        edit.lower_third(f"Test start {test_id - 18:02d}", f"Side current {trial['currentX']:+.2f} m/s", end=2.45)
        edit.stamp(2.6, "Clear", f"{seconds:.2f} seconds")
        edit.cue(2.6, "pass_soft")

    edit.shot("29-results", 15, "wide", final, at=2.8, rate=.46, track=True, alternative=learning,
              angle=26, sweep=-23)
    score_panel(edit, records)
    edit.say(.8, 5.8, f"Run {first['id']} was the breakthrough.")
    edit.say(6.4, 12.2, f"After {full['completedTrainingEpisodes']:,} runs, all 48 test starts got through.")
    edit.cue(6.4, "pass_soft")

    edit.shot("30-code", 11, "detail", final, at=2.7, rate=.48, angle=64, sweep=-73)
    edit.box(0, 0, 1920, 1080, DARK, alpha="88")
    edit.box(105, 276, 64, 4, MINT)
    edit.label(103, 304, "BEACON     EPISODE 01", size=24, color=MINT)
    edit.label(97, 366, "Try the experiment", size=83, font="Bahnschrift")
    edit.label(107, 477, "Unity project, PPO code and training data", size=34, bold=False)
    edit.label(110, 539, "GitHub link in the description", size=27, color=MINT, bold=False)
    edit.say(4.85, 10.2, "Next time, I'm giving him more than one gate.")
    edit.cue(6.8, "end")
    return edit



def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--data-dir", type=Path, help="Read the trace, replay and checkpoint from this directory.")
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--height", type=int, default=1080)
    args = parser.parse_args()
    args.directory.mkdir(parents=True, exist_ok=True)
    edit = assemble(args.data_dir or args.directory)
    edit.save(args.directory, args.width, args.height)
    print(f"Episode: {edit.clock:.2f}s, {len(edit.shots)} shots, {len(edit.subtitles)} captions.")


if __name__ == "__main__":
    main()
