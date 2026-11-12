using System;
using System.Collections.Generic;

namespace HarborLearning
{
    public enum Mission { Gate, Current, Slalom, Dock }
    public enum Outcome { Running, Passed, Collision, MissedGate, Timeout }

    public readonly struct Gate
    {
        public readonly float X, Z, Width;
        public Gate(float x, float z, float width) => (X, Z, Width) = (x, z, width);
    }

    public readonly struct Buoy
    {
        public readonly float X, Z, Radius;
        public readonly bool Port;
        public Buoy(float x, float z, float radius, bool port) =>
            (X, Z, Radius, Port) = (x, z, radius, port);
    }

    public sealed class Course
    {
        public readonly Mission Mission;
        public readonly Gate[] Gates;
        public readonly Buoy[] Buoys;
        public readonly float FinishZ;

        public Course(Mission mission)
        {
            Mission = mission;
            Gates = mission switch
            {
                Mission.Gate => new[] { new Gate(0, 16, 6.4f) },
                Mission.Current => new[] { new Gate(0, 20, 6.0f) },
                Mission.Slalom => new[] { new Gate(-4, 10, 6.2f), new Gate(3.6f, 22, 6.2f), new Gate(-2, 34, 6.2f) },
                Mission.Dock => new[] { new Gate(0, 19, 7.0f) },
                _ => throw new ArgumentOutOfRangeException(nameof(mission))
            };
            FinishZ = mission == Mission.Dock ? 34 : Gates[Gates.Length - 1].Z;
            var buoys = new List<Buoy>();
            foreach (Gate gate in Gates)
            {
                buoys.Add(new Buoy(gate.X - gate.Width / 2, gate.Z, .48f, true));
                buoys.Add(new Buoy(gate.X + gate.Width / 2, gate.Z, .48f, false));
            }
            if (mission == Mission.Dock)
            {
                for (int i = 0; i < 4; i++)
                {
                    buoys.Add(new Buoy(-2.9f, 29.5f + 2.4f * i, .65f, true));
                    buoys.Add(new Buoy(2.9f, 29.5f + 2.4f * i, .65f, false));
                }
            }
            Buoys = buoys.ToArray();
        }
    }

    public sealed class HarborEnvironment
    {
        public const float Dt = .05f;
        public const int ActionRepeat = 4;
        public const int ObservationSize = 13;
        public const float HullRadius = .74f;
        public const float HalfWidth = 15.5f;
        public const float NearZ = -4;
        public const float FarZ = 41;

        public Course Course { get; }
        public float X { get; private set; }
        public float Z { get; private set; }
        public float Yaw { get; private set; }
        public float Speed { get; private set; }
        public float TurnRate { get; private set; }
        public float CurrentX { get; private set; }
        public float CurrentZ { get; private set; }
        public int Tick { get; private set; }
        public int GateIndex { get; private set; }
        public Outcome Result { get; private set; }
        public float GroundSpeed => Distance(MathF.Sin(Yaw) * Speed + CurrentX, MathF.Cos(Yaw) * Speed + CurrentZ);
        public bool Docking => GateIndex == Course.Gates.Length && Course.Mission == Mission.Dock;
        public Gate Target => Docking ? new Gate(0, Course.FinishZ, 2.9f) :
            Course.Gates[Math.Min(GateIndex, Course.Gates.Length - 1)];

        private int dockTicks;

        public HarborEnvironment(Mission mission)
        {
            Course = new Course(mission);
            Reset(0);
        }

        public void Reset(int seed, int startGate = 0)
        {
            if (startGate < 0 || startGate >= Course.Gates.Length)
                throw new ArgumentOutOfRangeException(nameof(startGate));
            uint state = unchecked((uint)seed * 0x9E3779B9u + 0x85EBCA6Bu);
            X = (Uniform(ref state) * 2 - 1) * 2.4f;
            Z = 0;
            Yaw = (Uniform(ref state) * 2 - 1) * .32f;
            Speed = TurnRate = 0;
            Tick = dockTicks = 0;
            GateIndex = startGate;
            if (startGate > 0)
            {
                Gate previous = Course.Gates[startGate - 1];
                X = previous.X + X * .66f;
                Z = previous.Z + 1.25f;
            }
            Result = Outcome.Running;
            CurrentX = Course.Mission switch
            {
                Mission.Current => (.5f + .45f * Uniform(ref state)) * (seed % 2 == 0 ? 1 : -1),
                Mission.Slalom => (Uniform(ref state) * 2 - 1) * .22f,
                Mission.Dock => (Uniform(ref state) * 2 - 1) * .12f,
                _ => 0
            };
            CurrentZ = Course.Mission == Mission.Current ? (Uniform(ref state) * 2 - 1) * .12f : 0;
        }

        public float Step(float throttle, float rudder)
        {
            if (Result != Outcome.Running)
                throw new InvalidOperationException("Reset the episode before stepping a finished environment.");
            throttle = Math.Clamp(throttle, -1f, 1f);
            rudder = Math.Clamp(rudder, -1f, 1f);

            float startX = X;
            float startZ = Z;
            Gate target = Target;
            float oldDistance = Distance(target.X - X, target.Z - Z);
            Integrate(throttle, rudder);

            float distance = Distance(target.X - X, target.Z - Z);
            float reward = .10f * (oldDistance - distance) - .0015f;
            if (MathF.Abs(X) > HalfWidth - HullRadius || Z < NearZ + HullRadius || Z > FarZ - HullRadius)
                return Finish(Outcome.Collision, reward - 1.2f);
            foreach (Buoy buoy in Course.Buoys)
            {
                if (TouchesBuoy(startX, startZ, buoy))
                    return Finish(Outcome.Collision, reward - 1.2f);
            }

            if (!Docking && startZ < target.Z && Z >= target.Z)
            {
                float crossingX = startX + (X - startX) * ((target.Z - startZ) / (Z - startZ));
                if (MathF.Abs(crossingX - target.X) > target.Width / 2 - HullRadius - .48f)
                    return Finish(Outcome.MissedGate, reward - .85f);
                GateIndex++;
                if (GateIndex == Course.Gates.Length && Course.Mission != Mission.Dock)
                    return Finish(Outcome.Passed, reward + 2f);
                reward += .55f;
            }
            else if (Docking)
            {
                bool inBerth = MathF.Abs(X) < 1.35f && MathF.Abs(Z - Course.FinishZ) < 1.55f;
                bool aligned = MathF.Abs(Yaw) < .28f;
                bool stopped = GroundSpeed < .38f && MathF.Abs(TurnRate) < .25f;
                dockTicks = inBerth && aligned && stopped ? dockTicks + 1 : 0;
                if (dockTicks >= 10)
                    return Finish(Outcome.Passed, reward + 2f);
            }

            int limit = Course.Mission == Mission.Slalom || Course.Mission == Mission.Dock ? 980 : 640;
            return Tick >= limit ? Finish(Outcome.Timeout, reward - .35f) : reward;
        }

        public void CoastAfterPass()
        {
            if (Result != Outcome.Passed)
                throw new InvalidOperationException("A finishing coast requires a successful run.");
            Integrate(0, 0);
        }

        private void Integrate(float throttle, float rudder)
        {
            Speed += (1.8f * throttle - .6f * Speed) * Dt;
            TurnRate += (1.2f * rudder - .5f * TurnRate) * Dt;
            Yaw = Wrap(Yaw + TurnRate * Dt);
            X += (MathF.Sin(Yaw) * Speed + CurrentX) * Dt;
            Z += (MathF.Cos(Yaw) * Speed + CurrentZ) * Dt;
            Tick++;
        }

        public void Observe(float[] observation)
        {
            Gate target = Target;
            float dx = target.X - X;
            float dz = target.Z - Z;
            float s = MathF.Sin(Yaw);
            float c = MathF.Cos(Yaw);
            observation[0] = dx / 18;
            observation[1] = dz / 36;
            observation[2] = (c * dx - s * dz) / 18;
            observation[3] = (s * dx + c * dz) / 36;
            observation[4] = s;
            observation[5] = c;
            observation[6] = Speed / 3;
            observation[7] = TurnRate / 2.4f;
            observation[8] = CurrentX;
            observation[9] = CurrentZ;
            observation[10] = target.Width / 8;
            observation[11] = X / HalfWidth;
            observation[12] = Docking ? 1 : 0;
        }

        public static float Wrap(float angle) => MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));
        public static float Distance(float x, float z) => MathF.Sqrt(x * x + z * z);

        private float Finish(Outcome result, float reward)
        {
            Result = result;
            return reward;
        }

        private bool TouchesBuoy(float startX, float startZ, Buoy buoy)
        {
            float dx = X - startX;
            float dz = Z - startZ;
            float lengthSquared = dx * dx + dz * dz;
            float progress = lengthSquared > 0 ?
                Math.Clamp(((buoy.X - startX) * dx + (buoy.Z - startZ) * dz) / lengthSquared, 0, 1) : 0;
            float ox = buoy.X - (startX + progress * dx);
            float oz = buoy.Z - (startZ + progress * dz);
            float radius = buoy.Radius + HullRadius;
            return ox * ox + oz * oz <= radius * radius;
        }

        private static float Uniform(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) * (1f / 16777216f);
        }
    }
}
