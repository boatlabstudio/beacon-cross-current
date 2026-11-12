using System.Text.Json;
using HarborLearning;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
if (args.Length == 2 && args[0] == "--demo")
{
    Demonstrations.Write(args[1], json);
    return;
}
if (args.Length != 2 || !Enum.TryParse(args[0], true, out Mission mission) ||
    !Enum.IsDefined(mission) || !int.TryParse(args[1], out int count) || count < 1 || count > 256)
{
    Console.Error.WriteLine("Usage: HarborHost <gate|current|slalom|dock> <environment count>");
    Environment.ExitCode = 2;
    return;
}

var environments = Enumerable.Range(0, count).Select(_ => new HarborEnvironment(mission)).ToArray();
var observations = environments.Select(_ => new float[HarborEnvironment.ObservationSize]).ToArray();
while (Console.ReadLine() is { } line)
{
    try
    {
        var command = JsonSerializer.Deserialize<Command>(line, json)
            ?? throw new ArgumentException("Missing command.");
        if (command.Seeds?.Length != count)
            throw new ArgumentException($"Provide {count} reset seeds.");
        int gateCount = environments[0].Course.Gates.Length;
        if (command.StartGates != null && (command.StartGates.Length != count ||
            command.StartGates.Any(gate => gate < 0 || gate >= gateCount)))
            throw new ArgumentException("Invalid starting gates for this mission.");
        switch (command.Op)
        {
            case "reset":
                for (int i = 0; i < count; i++)
                {
                    environments[i].Reset(command.Seeds[i], command.StartGates?[i] ?? 0);
                    environments[i].Observe(observations[i]);
                }
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    observations,
                    poses = environments.Select(Recording.Pose),
                    currents = environments.Select(e => new[] { e.CurrentX, e.CurrentZ })
                }, json));
                break;
            case "step":
                if (command.Actions?.Length != count ||
                    command.Actions.Any(a => a.Length != 2 || a.Any(x => !float.IsFinite(x))))
                    throw new ArgumentException($"Provide {count} pairs of finite throttle and rudder values.");
                if (command.PostPassTicks is < 0 or > 60)
                    throw new ArgumentException("Post-pass capture requires 0–60 physics ticks.");
                var rewards = new float[count];
                var results = new int[count];
                var poses = new Frame[count];
                var resets = command.RecordResets ? new ResetFrame?[count] : null;
                var coasts = command.PostPassTicks > 0 ? new Frame[]?[count] : null;
                for (int i = 0; i < count; i++)
                {
                    HarborEnvironment env = environments[i];
                    for (int tick = 0; tick < HarborEnvironment.ActionRepeat && env.Result == Outcome.Running; tick++)
                        rewards[i] += env.Step(command.Actions[i][0], command.Actions[i][1]);
                    poses[i] = Recording.Pose(env);
                    results[i] = (int)env.Result;
                    if (env.Result != Outcome.Running)
                    {
                        if (coasts != null && env.Result == Outcome.Passed)
                        {
                            var coast = new Frame[command.PostPassTicks];
                            for (int tick = 0; tick < coast.Length; tick++)
                            {
                                env.CoastAfterPass();
                                coast[tick] = Recording.Pose(env);
                            }
                            coasts[i] = coast;
                        }
                        env.Reset(command.Seeds[i], command.StartGates?[i] ?? 0);
                        if (resets != null)
                            resets[i] = new ResetFrame(Recording.Pose(env), env.CurrentX, env.CurrentZ);
                    }
                    env.Observe(observations[i]);
                }
                Console.WriteLine(JsonSerializer.Serialize(new { observations, rewards, results, poses, resets, coasts }, json));
                break;
            default:
                throw new ArgumentException("Unknown command.");
        }
    }
    catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, json));
    }
}

internal sealed record Command(string Op, int[]? Seeds, float[][]? Actions, int[]? StartGates,
                               bool RecordResets, int PostPassTicks);
internal sealed record Frame(int Tick, float X, float Z, float Yaw, float Speed);
internal sealed record ResetFrame(Frame Pose, float CurrentX, float CurrentZ);
internal sealed record Attempt(int Id, int Seed, string Phase, int Outcome, float CurrentX, float CurrentZ, Frame[] Frames);
internal sealed record ReplaySet(string Mission, string Source, Attempt[] Attempts);

internal static class Recording
{
    public static Frame Pose(HarborEnvironment env) => new(env.Tick, env.X, env.Z, env.Yaw, env.GroundSpeed);
}
