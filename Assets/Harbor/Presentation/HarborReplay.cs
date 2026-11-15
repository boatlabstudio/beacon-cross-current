using System;
using UnityEngine;

namespace HarborLearning
{
    [Serializable]
    public sealed class HarborReplay
    {
        public string mission;
        public string source;
        public HarborAttempt[] attempts;

        public static HarborReplay Load(Mission mission)
        {
            string name = mission.ToString().ToLowerInvariant();
            TextAsset file = Resources.Load<TextAsset>("Replays/" + name);
            if (!file)
                file = Resources.Load<TextAsset>("Demonstrations/" + name);
            return file ? JsonUtility.FromJson<HarborReplay>(file.text) : null;
        }
    }

    [Serializable]
    public sealed class HarborAttempt
    {
        public int id;
        public int seed;
        public string phase;
        public int trainingSteps;
        public int outcome;
        public float currentX;
        public float currentZ;
        public HarborFrame[] frames;
    }

    [Serializable]
    public struct HarborFrame
    {
        public int tick;
        public int gateIndex;
        public float x, z, yaw, speed;

        public static HarborFrame From(HarborEnvironment env) => new HarborFrame
        {
            tick = env.Tick, x = env.X, z = env.Z, yaw = env.Yaw, speed = env.GroundSpeed, gateIndex = env.GateIndex
        };

        public static HarborFrame Between(HarborFrame a, HarborFrame b, float tick)
        {
            float t = Mathf.InverseLerp(a.tick, b.tick, tick);
            return new HarborFrame
            {
                tick = Mathf.RoundToInt(tick),
                x = Mathf.Lerp(a.x, b.x, t),
                z = Mathf.Lerp(a.z, b.z, t),
                yaw = a.yaw + HarborEnvironment.Wrap(b.yaw - a.yaw) * t,
                speed = Mathf.Lerp(a.speed, b.speed, t),
                gateIndex = tick >= b.tick ? b.gateIndex : a.gateIndex
            };
        }

        public Vector3 RoutePoint => new Vector3(x, .054f, z);
    }
}
