"""Drive the same deterministic C# environment used by the Unity scene."""

import json
from pathlib import Path
import subprocess

import numpy as np


DEFAULT_HOST = (
    Path(__file__).resolve().parent
    / "HarborHost/bin/Release/net10.0/HarborHost.dll"
)


class HarborBatch:
    def __init__(self, mission, count, host=DEFAULT_HOST):
        self.count = count
        self.process = subprocess.Popen(
            ["dotnet", str(host), mission, str(count)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            encoding="utf-8",
        )

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
        finally:
            self.process.stdout.close()

    def request(self, op, seeds, actions=None, start_gates=None, record_resets=False, post_pass_ticks=0):
        message = {"op": op, "seeds": np.asarray(seeds).tolist()}
        if actions is not None:
            message["actions"] = np.asarray(actions).tolist()
        if start_gates is not None:
            message["startGates"] = np.asarray(start_gates).tolist()
        if record_resets:
            message["recordResets"] = True
        if post_pass_ticks:
            message["postPassTicks"] = post_pass_ticks
        self.process.stdin.write(json.dumps(message, separators=(",", ":")) + "\n")
        self.process.stdin.flush()
        line = self.process.stdout.readline()
        if not line:
            raise RuntimeError(f"HarborHost stopped (exit code {self.process.poll()}).")
        response = json.loads(line)
        if "error" in response:
            raise RuntimeError(response["error"])
        response["observations"] = np.asarray(response["observations"], dtype=np.float32)
        return response
