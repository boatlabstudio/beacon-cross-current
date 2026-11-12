using System.Text.Json;
using HarborLearning;

internal static class Demonstrations
{
    public static void Write(string directory, JsonSerializerOptions json)
    {
        Directory.CreateDirectory(directory);
        foreach (Mission mission in Enum.GetValues<Mission>())
        {
            var environment = new HarborEnvironment(mission);
            var attempts = new List<Attempt>();
            for (int i = 0; i < 8; i++)
            {
                int seed = 1200 + i * 17;
                environment.Reset(seed);
                var frames = new List<Frame> { Recording.Pose(environment) };
                bool reference = i >= 5;
                while (environment.Result == Outcome.Running)
                {
                    (float throttle, float rudder) = reference ? ReferenceAction(environment) : TrialAction(environment, i);
                    environment.Step(throttle, rudder);
                    if (environment.Tick % HarborEnvironment.ActionRepeat == 0 || environment.Result != Outcome.Running)
                        frames.Add(Recording.Pose(environment));
                }
                attempts.Add(new Attempt(i + 1, seed, reference ? "reference" : "trial", (int)environment.Result,
                    environment.CurrentX, environment.CurrentZ, frames.ToArray()));
            }
            string key = mission.ToString().ToLowerInvariant();
            var replay = new ReplaySet(key, "controller", attempts.ToArray());
            File.WriteAllText(Path.Combine(directory, key + ".json"), JsonSerializer.Serialize(replay, json));
        }
    }

    private static (float throttle, float rudder) TrialAction(HarborEnvironment env, int trial)
    {
        if (trial == 4)
            return (.08f, 0);
        float bias = trial switch { 0 => -6.0f, 1 => 3.0f, 2 => 5.2f, _ => -2.9f };
        float desired = MathF.Atan2(env.Target.X + bias - env.X, MathF.Max(2f, env.Target.Z - env.Z));
        float rudder = 1.7f * HarborEnvironment.Wrap(desired - env.Yaw) - .95f * env.TurnRate;
        return (.85f, rudder);
    }

    private static (float throttle, float rudder) ReferenceAction(HarborEnvironment env)
    {
        float dz = env.Target.Z - env.Z;
        float dx = env.Target.X - env.X;
        float speed = env.Docking ? Math.Clamp(dz * .5f, -.65f, 1.15f) : 2.15f;
        float lateral = Math.Clamp(dx * .68f - env.CurrentX, -1.5f, 1.5f);
        float heading = MathF.Atan2(lateral, MathF.Max(.72f, MathF.Abs(speed)));
        if (env.Docking && MathF.Abs(dz) < 1.3f && MathF.Abs(dx) < .45f)
            heading = 0;
        float throttle = (speed - env.Speed) * .92f + speed / 3;
        float rudder = 1.85f * HarborEnvironment.Wrap(heading - env.Yaw) - 1.35f * env.TurnRate;
        return (throttle, rudder);
    }
}
