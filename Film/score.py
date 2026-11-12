"""Mix the episode's sound design with its original or licensed score."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import wave

import numpy as np


RATE = 48_000
TAU = 2 * np.pi


def seconds(length):
    return np.arange(round(length * RATE), dtype=np.float32) / RATE


def frequency(note):
    return 440 * 2 ** ((note - 69) / 12)


def fade(t, attack, release):
    return np.minimum(1, t / attack) * np.clip((t[-1] - t) / release, 0, 1)


def put(track, at, sound, gain=1, pan=0):
    index = round(at * RATE)
    available = min(len(sound), len(track) - index)
    if available <= 0:
        return
    track[index:index + available, 0] += sound[:available] * gain * np.sqrt((1 - pan) / 2)
    track[index:index + available, 1] += sound[:available] * gain * np.sqrt((1 + pan) / 2)


def pad(note, length, spread):
    t = seconds(length)
    hz = frequency(note)
    phase = TAU * hz * t
    out = (.59 * np.sin(phase) + .26 * np.sin(phase * (1 + spread)) +
           .09 * np.sin(2 * phase) + .06 * np.sin(3 * phase))
    return out * fade(t, min(1.2, length / 2), min(1.75, length / 2)) * (.83 + .17 * np.sin(t * 2.3))


def pluck(note, length=1.7):
    t = seconds(length)
    phase = TAU * frequency(note) * t
    out = (.65 * np.sin(phase + .4) * np.exp(-2.6 * t)
           + .23 * np.sin(1.999 * phase) * np.exp(-5.4 * t)
           + .09 * np.sin(3.013 * phase + .6) * np.exp(-9.8 * t)
           + .04 * np.sin(4.026 * phase) * np.exp(-17 * t))
    return out * fade(t, .009, .27)


def kick(length=.33, strength=1):
    t = seconds(length)
    phase = TAU * (42 * t + 59 * .035 * (1 - np.exp(-t / .035)))
    return strength * np.sin(phase) * np.exp(-12 * t) * fade(t, .008, .06)


def texture(rng, length, low, high):
    sample = rng.normal(size=round(length * RATE))
    frequency_bins = np.fft.rfftfreq(len(sample), 1 / RATE)
    bands = np.minimum(1, (frequency_bins / low) ** 2) / (1 + (frequency_bins / high) ** 3)
    result = np.fft.irfft(np.fft.rfft(sample) * bands).astype(np.float32)
    return result / max(np.max(np.abs(result)), 1e-5)


def arrangement(track, starts, events, passes, total, rng):
    rush = events["accelerate"]
    brake = events["brake"]
    first_pass, clear = passes
    # Leave the cold open to the water and the boat. The plucked theme returns
    # in a higher register each time the score has earned a release.
    sections = (
        (events["title"], starts["05-first-launch"], 99, 99, .84, True),
        (starts["05-first-launch"], starts["09-missed-gate"], 86, 86, .63, False),
        (starts["09-missed-gate"], starts["12-neural-network"], 92, 95, .76, False),
        (starts["12-neural-network"], rush, 73, 73, .57, True),
        (rush, brake, 108, 154, 1.07, True),
        (brake, first_pass, 81, 91, .64, False),
        (first_pass, starts["21-mid-training-decision"], 102, 102, .94, True),
        (starts["21-mid-training-decision"], starts["24-final-exterior"], 91, 91, .70, False),
        (starts["24-final-exterior"], clear, 83, 83, .56, False),
        (clear, starts["30-code"], 105, 105, .98, True),
        (starts["30-code"], total, 84, 84, .71, True),
    )
    minor = ((38, 45, 48, 52, 53), (34, 41, 45, 48, 50),
             (41, 48, 52, 55, 57), (36, 43, 46, 50, 55))
    major = ((41, 48, 52, 55, 57), (36, 43, 47, 50, 52),
             (38, 45, 48, 52, 53), (34, 41, 45, 48, 50))
    brush = texture(rng, .18, 1250, 6800) * fade(seconds(.18), .012, .133)
    rim = texture(rng, .15, 490, 2200) * fade(seconds(.15), .004, .105)
    soft_kick = kick(.28, .7)
    downbeat = kick(.38)
    for start, stop, bpm, end_bpm, energy, melody in sections:
        at = start
        bar = 0
        while at < stop:
            tempo = bpm + (end_bpm - bpm) * (at - start) / (stop - start)
            beat = 60 / tempo
            length = min(beat * 4, stop - at)
            notes = (major if melody else minor)[bar % 4]
            if length > .12:
                for i, note in enumerate(notes):
                    put(track, at, pad(note, length + .68, .0018 + i * .0008),
                        energy * (.070 if i == 0 else .026), (i - 2) * .27)
            beats = 8 if start == rush else 4
            for i in range(beats):
                position = i * 4 / beats
                when = at + beat * position
                if when >= stop:
                    break
                if i % (beats // 2) == 0:
                    put(track, when, downbeat if i == 0 else soft_kick, energy * .092)
                elif i % 2 or beats == 4:
                    put(track, when, brush if i % 2 else rim, energy * .041, -.28 + i * .095)
                if i < beats - 1 and start == rush:
                    put(track, when + beat * .24, brush, energy * .032, .35)
            if melody:
                if bar % 4 == 0:
                    phrase = ((.48, 74), (1.22, 76), (1.96, 81), (2.96, 79), (3.47, 76))
                elif bar % 4 == 1:
                    phrase = ((.3, 72), (1.49, 76), (2.47, 74))
                else:
                    phrase = ((.58, notes[3] + 24), (1.52, notes[1] + 24), (2.35, notes[2] + 24))
            else:
                phrase = ((.52, notes[1] + 12), (2.1, notes[3] + 12))
            for position, note in phrase:
                when = max(start, at + position * beat + rng.uniform(-.019, .019))
                if when >= stop:
                    continue
                sound = pluck(note)
                put(track, when, sound, energy * rng.uniform(.074, .105), -.29)
                put(track, when + beat * .74, sound, energy * .025, .55)
            at += beat * 4
            bar += 1

    for end, length in ((first_pass, 3.8), (clear, 2.4)):
        a, b = round((end - length) * RATE), round(end * RATE)
        track[a:b] *= np.linspace(1, .15, b - a, dtype=np.float32)[:, None]


def read_music(path, audio_filter=None):
    if not path.is_file():
        raise FileNotFoundError(f"Missing music file: {path}")
    ffmpeg = os.environ.get("BEACON_FFMPEG", "ffmpeg")
    args = [ffmpeg, "-nostdin", "-v", "error", "-i", str(path)]
    if audio_filter:
        args.extend(["-af", audio_filter])
    args.extend(["-vn", "-f", "f32le", "-ar", str(RATE), "-ac", "2", "pipe:1"])
    decoded = subprocess.run(args, check=True, stdout=subprocess.PIPE).stdout
    return np.frombuffer(decoded, dtype="<f4").reshape(-1, 2)


def licensed_arrangement(mix, folder, starts, events, passes):
    """Place musical phrases against the filmed action, leaving the finishes room to breathe."""
    practice_path = folder / "special-ops.mp3"
    practice = read_music(practice_path)
    theme = read_music(folder / "tomorrow.mp3")
    rush, brake = events["accelerate"], events["brake"]
    first_pass, clear = passes

    def place(recording, start, end, source, gain_db, intro, tail):
        index = round(start * RATE)
        length = round(end * RATE) - index
        source_index = round(source * RATE)
        excerpt = recording[source_index:source_index + length]
        if len(excerpt) < length:
            raise ValueError("The cue extends past the end of the music file.")
        t = np.arange(length, dtype=np.float32) / RATE
        envelope = np.minimum(t / intro, 1) * np.clip((length / RATE - t) / tail, 0, 1)
        mix[index:index + length] += excerpt * (10 ** (gain_db / 20) * envelope)[:, None]

    place(theme, events["title"], 29.6, 10.0, 2.5, .35, 1.2)
    place(practice, starts["05-first-launch"], 46.1, 33.1, 2.8, .35, .85)
    place(practice, 49.6, 70.35, 81.6, 2.1, .45, 1.2)
    place(practice, starts["10-reward"], 84.35, 58.1, 2.7, .45, .85)
    place(practice, starts["12-neural-network"], rush + .12, 161.1, 2.8, .7, .24)

    # atempo changes the playback tempo without shifting the instruments' pitch.
    acceleration = read_music(
        practice_path,
        "atrim=start=175.2:end=189.5,asetpts=PTS-STARTPTS,"
        "asendcmd=c='2.8 atempo tempo 1.12;5.8 atempo tempo 1.19;8.7 atempo tempo 1.26',atempo=1.04",
    )
    place(acceleration, rush - .10, brake, 0, 3.0, .25, .47)
    place(practice, 125.7, first_pass - 3.8, 62.8, 1.7, .55, 2.7)
    place(theme, first_pass - .04, 186.75, 49.0, 3.0, .20, 2.4)
    place(theme, starts["22-route-comparison"] + .2, clear - 3.05, 12.5, 1.15, .65, 2.9)
    place(theme, clear - .08, len(mix) / RATE, 78.5, 2.3, .18, 3.2)

    # The deck camera changes perspective. Let its propeller and water rise through the score.
    for start, end, depth in ((233., 243.5, .30), (253., 261.5, .24)):
        a, b = round(start * RATE), round(end * RATE)
        t = np.arange(b - a, dtype=np.float32) / RATE
        duck = np.minimum(np.clip(t / .72, 0, 1), np.clip((end - start - t) / .65, 0, 1))
        mix[a:b] *= (1 - depth * duck)[:, None]


def compose(directory, music_dir=None, output=None):
    sound = json.loads((directory / "sound.json").read_text(encoding="utf-8"))
    shots = json.loads((directory / "shots.json").read_text(encoding="utf-8"))["shots"]
    total = sound["duration"]
    mix = np.zeros((round(total * RATE), 2), dtype=np.float32)
    rng = np.random.default_rng(10711)
    events = {cue["kind"]: cue["time"] for cue in sound["cues"]}
    starts = {}
    at = 0
    for shot in shots:
        starts[shot["name"]] = at
        at += shot["length"]
    passes = [cue["time"] for cue in sound["cues"] if cue["kind"] == "pass"]
    if music_dir is None:
        arrangement(mix, starts, events, passes, total, rng)
    else:
        licensed_arrangement(mix, music_dir, starts, events, passes)

    # Foley is driven by the recorded hull speed and the chosen camera.
    wash = texture(rng, 11, 90, 940)
    t0 = 0
    for shot in shots:
        length = shot["length"]
        t = seconds(length)
        ticks = (shot["offset"] + t * shot["rate"]) / .05
        frames = shot["attempt"]["frames"]
        speed = np.interp(ticks, [f["tick"] for f in frames], [f["speed"] for f in frames]).astype(np.float32)
        moving = (ticks <= frames[-1]["tick"]) & (shot["rate"] > 0)
        strength = np.minimum(speed / 3, 1) * moving
        bow = shot["camera"] == "onboard"
        env = fade(t, .14, .2)
        indices = (np.arange(len(t)) + round(t0 * RATE)) % len(wash)
        perspective = 1.32 if music_dir else 1
        put(mix, t0, wash[indices] * env * (.45 + .65 * strength),
            perspective * (.100 if bow else .048), .20)
        phase = np.cumsum(TAU * (49 + 38 * strength) / RATE, dtype=np.float64)
        motor = (.7 * np.sin(phase) + .25 * np.sin(2 * phase) + .05 * np.sin(3 * phase))
        put(mix, t0, motor * env * strength, perspective * (.063 if bow else .037), -.13)
        t0 += length

    for cue in sound["cues"]:
        at, kind = cue["time"], cue["kind"]
        if music_dir is not None and kind not in ("impact", "miss"):
            continue
        if kind == "impact":
            t = seconds(.48)
            body = (.65 * np.sin(TAU * 74 * t) + .35 * np.sin(TAU * 121 * t)) * np.exp(-18 * t)
            grit = texture(rng, .48, 200, 2300) * np.exp(-11 * t)
            put(mix, at, (body + grit * .36) * fade(t, .008, .1), .35, -.24)
        elif kind == "pass":
            for i, note in enumerate((62, 69, 76, 81)):
                put(mix, at + i * .105, pluck(note, 2), .142, (i - 1.5) * .23)
        elif kind in ("pass_soft", "insight"):
            put(mix, at, pluck(76 if kind == "pass_soft" else 81), .07, .20)
        elif kind == "miss":
            put(mix, at, pluck(48, 1.5), .125, -.20)
        elif kind == "end":
            put(mix, at, pluck(74, 2.8), .083)
        elif kind in ("accelerate", "brake"):
            t = seconds(1.0)
            bend = (115 * t + 95 * t * t) if kind == "accelerate" else (170 * t - 64 * t * t)
            put(mix, at, np.sin(TAU * bend) * fade(t, .18, .38), .12)
    ramp = round(2 * RATE)
    mix[-ramp:] *= np.linspace(1, 0, ramp, dtype=np.float32)[:, None]
    if music_dir is None:
        np.tanh(mix * 2.1, out=mix)
        mix *= .83 / np.max(np.abs(mix))
    else:
        # A soft ceiling retains the difference between quiet maneuvers and arrivals.
        np.tanh(mix * 1.38, out=mix)
        mix *= min(1., .83 / np.max(np.abs(mix)))
    with wave.open(str(output or directory / "episode-score.wav"), "wb") as file:
        file.setnchannels(2)
        file.setsampwidth(2)
        file.setframerate(RATE)
        file.writeframes((mix * 32767).astype("<i2").tobytes())
    print(f"Stereo score and effects: {total:.2f}s")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--music-dir", type=Path, help="Use Special Ops and Tomorrow from the credited composer.")
    parser.add_argument("--output", type=Path, help="Path for the mixed stereo WAV.")
    args = parser.parse_args()
    compose(args.directory, args.music_dir, args.output)
