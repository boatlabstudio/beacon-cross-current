"""Draw the actor and trace signed input-to-command contributions."""

import math

import numpy as np


INPUTS = (
    "Gate offset X", "Gate offset Z", "Gate right", "Gate ahead", "Heading sine",
    "Heading cosine", "Water speed", "Yaw rate", "Current X", "Current Z",
    "Gate width", "Basin position", "Dock mode",
)
COMMANDS = ("Throttle", "Rudder")
POSITIVE = "125A7A"
NEGATIVE = "A14331"
INK = "20292B"
PAPER = "F5F1E6"
FAINT = "52615C"
GRID = "BEBCAF"


def dot(x, y, radius):
    k = round(radius * .552)
    return (f"m {x+radius} {y} b {x+radius} {y+k} {x+k} {y+radius} {x} {y+radius} "
            f"{x-k} {y+radius} {x-radius} {y+k} {x-radius} {y} "
            f"{x-radius} {y-k} {x-k} {y-radius} {x} {y-radius} "
            f"{x+k} {y-radius} {x+radius} {y-k} {x+radius} {y}")


def link(start, end, width):
    x, y = start
    a, b = end
    half = max(1, round(width / 2))
    return f"m {x} {y-half} l {a} {b-half} {a} {b+half} {x} {y+half} {x} {y-half}"


class Network:
    def __init__(self, checkpoint):
        with np.load(checkpoint) as data:
            self.w1 = data["actor_w1"].astype(np.float64)
            self.b1 = data["actor_b1"].astype(np.float64)
            self.w2 = data["actor_w2"].astype(np.float64)
            self.b2 = data["actor_b2"].astype(np.float64)

    def explain(self, observation, command):
        # Integrate each two-edge path from zero input to the current observation.
        # Along that line the tanh derivative has a closed-form average.
        contributions = np.asarray(observation)[:, None] * self.w1
        change = contributions.sum(axis=0)
        baseline = np.tanh(self.b1)
        hidden = np.tanh(self.b1 + change)
        slope = np.divide(hidden - baseline, change, out=1 - baseline**2, where=np.abs(change) > 1e-9)
        paths = contributions * slope * self.w2[:, command]
        return hidden @ self.w2 + self.b2, paths

    def samples(self, edit, attempt, command, start, end, fixed, count):
        interval = end - start if fixed else .5
        shot = edit.shots[-1]
        signals = attempt["signals"]
        for i in range(math.ceil((end - start) / interval)):
            a = start + i * interval
            b = min(end, a + interval)
            clip_time = shot["offset"] + a * shot["rate"]
            signal = signals[0 if fixed else min(len(signals) - 1, int(clip_time / .2 + 1e-6))]
            output, paths = self.explain(signal["observation"], command)
            np.testing.assert_allclose(output, signal["action"], rtol=1e-4, atol=2e-5)
            strongest = np.argsort(np.abs(paths), axis=None)[-count:][::-1]
            selected = [(*np.unravel_index(key, paths.shape), paths.flat[key]) for key in strongest]
            yield a, b, np.clip(output, -1, 1), selected

    def draw(self, edit, attempt, *, title, command, start, end, inset=False, fixed=False):
        samples = self.samples(edit, attempt, command, start, end, fixed, 3 if inset else 5)
        if inset:
            self.draw_card(edit, samples, title, command, start, end)
        else:
            self.draw_sheet(edit, samples, title, command, start, end)

    @staticmethod
    def draw_card(edit, samples, title, command, start, end):
        def label(x, y, value, size=24, color=INK, *, a=start, b=end, bold=True, anchor=7, font="Segoe UI"):
            edit.label(x, y, value, size=size, color=color, start=a, end=b, fade=0, layer=7,
                       bold=bold, anchor=anchor, font=font)

        def rule(x, y, width, a=start, b=end, color=GRID, height=1):
            edit.box(x, y, width, height, color, start=a, end=b, layer=5)

        x, y, w, h = 1214, 53, 654, 342
        edit.box(x + 4, y + 5, w, h, "182A30", alpha="80", start=start, end=end, layer=2)
        edit.box(x, y, w, h, PAPER, start=start, end=end, layer=3)
        rule(x, y, w, height=4, color=INK)
        label(x + 27, y + 21, title, 37, font="Georgia")
        label(x + 29, y + 74, f"Three strongest paths to {COMMANDS[command].lower()}", 22, FAINT, bold=False)
        rule(x + 28, y + 113, w - 56, color=INK)
        label(x + 29, y + 125, "OBSERVATION", 17, FAINT)
        label(x + 293, y + 125, "HIDDEN UNIT", 17, FAINT)
        label(x + w - 28, y + 125, "CONTRIBUTION", 17, FAINT, anchor=9)
        for index in range(3):
            row_y = y + 152 + index * 38
            rule(x + 28, row_y + 34, w - 56)
        rule(x + 28, y + 274, w - 56, color=INK)
        label(x + 29, y + 292, "Applied " + COMMANDS[command].lower(), 23)
        for a, b, output, selected in samples:
            for i, (input_id, unit_id, value) in enumerate(selected):
                row_y = y + 152 + i * 38
                color = POSITIVE if value >= 0 else NEGATIVE
                label(x + 29, row_y, INPUTS[input_id], 21, a=a, b=b, bold=False)
                label(x + 255, row_y, "→", 21, color, a=a, b=b)
                label(x + 293, row_y, f"#{unit_id + 1:02d}", 21, a=a, b=b, bold=False)
                label(x + w - 29, row_y, f"{value:+.3f}", 23, color, a=a, b=b, anchor=9)
            applied = output[command]
            color = POSITIVE if applied >= 0 else NEGATIVE
            rule(x + 337, y + 311, 164, a, b)
            rule(x + 419, y + 307, 2, a, b, height=9, color=FAINT)
            if abs(applied) >= .01:
                rule(x + 419 + round(min(0, applied * 82)), y + 309, round(abs(applied) * 82),
                     a, b, color=color, height=4)
            label(x + w - 29, y + 287, f"{applied:+.2f}", 33, color, a=a, b=b, anchor=9)

    @staticmethod
    def draw_sheet(edit, samples, title, command, start, end):
        def label(x, y, value, size=26, color=INK, *, a=start, b=end, bold=True, anchor=7, font="Segoe UI"):
            edit.label(x, y, value, size=size, color=color, start=a, end=b, fade=0, layer=7,
                       bold=bold, anchor=anchor, font=font)

        def rule(x, y, width, height=1, color=GRID):
            edit.box(x, y, width, height, color, start=start, end=end, layer=4)

        def nodes(points, radius, color, a=start, b=end, layer=5):
            edit.shape(" ".join(dot(*point, radius) for point in points), color, start=a, end=b, layer=layer)

        edit.box(0, 0, 1920, 1080, PAPER, start=start, end=end, layer=2)
        edit.box(0, 933, 1920, 147, INK, start=start, end=end, layer=3)
        label(120, 66, title, 70, font="Georgia")
        label(122, 163, "Test start 02  ·  Before training", 28, FAINT, bold=False)
        rule(121, 221, 1678, height=2, color=INK)
        label(126, 251, "13 observations", 34, font="Georgia")
        label(811, 251, "48 hidden units", 34, font="Georgia")
        label(1345, 251, "2 commands", 34, font="Georgia")
        inputs = [(505, 347 + i * 36) for i in range(len(INPUTS))]
        hidden = [(945, 342 + i * 9) for i in range(48)]
        outputs = [(1327, 415), (1327, 649)]
        for i, label_name in enumerate(INPUTS):
            y = inputs[i][1]
            label(127, y, label_name, 22, anchor=4, bold=False)
            rule(312, y, 165)
        nodes(inputs, 5, GRID)
        nodes(hidden, 3, GRID)
        nodes(outputs, 8, FAINT)
        label(1373, 381, "Throttle", 35, font="Georgia")
        label(1373, 615, "Rudder", 35, font="Georgia")
        for a, b, output, selected in samples:
            outbound = {}
            for input_id, unit, value in reversed(selected):
                color = POSITIVE if value >= 0 else NEGATIVE
                edit.shape(link(inputs[input_id], hidden[unit], 3), color, start=a, end=b, layer=6)
                nodes([inputs[input_id], hidden[unit]], 6, color, a, b, layer=7)
                outbound[unit] = outbound.get(unit, 0.) + value
            for unit, value in outbound.items():
                color = POSITIVE if value >= 0 else NEGATIVE
                edit.shape(link(hidden[unit], outputs[command], 3), color, start=a, end=b, layer=6)
            for index, value in enumerate(output):
                label(1373, (415, 649)[index] + 17, f"{value:+.2f}", 60, a=a, b=b)
        rule(121, 837, 1678)
        label(123, 865, "Increases the command", 22, POSITIVE)
        label(420, 865, "Decreases the command", 22, NEGATIVE)
        label(718, 865, "Shown paths have the largest contributions relative to zero input.", 22, FAINT, bold=False)
