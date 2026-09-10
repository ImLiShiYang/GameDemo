using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class NetworkSnapshotInterpolationChecks
{
    [MenuItem("Tools/Network Validation/Run Snapshot Interpolation Checks")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run interpolation checks outside Play mode.");
        List<string> results = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); results.Add(label); }
        SnapshotInterpolationBuffer buffer = new SnapshotInterpolationBuffer();
        Check(!buffer.Advance(0.05, 0.2, out _), "Empty buffer has no sample");
        buffer.Push(Frame(100, 0f, 350f), 0.2, 5f, out bool reset);
        Check(reset && Math.Abs(buffer.PlaybackTime - 4.8) < 0.00001, "First snapshot seeds delayed server timeline");
        buffer.Push(Frame(102, 1f, 10f), 0.2, 5f, out reset);
        Check(!reset, "Normal snapshot does not reset playback clock");
        buffer.Advance(0, 0.2, out InterpolationSnapshot sample);
        Check(sample.Position == Vector3.zero, "Startup holds first known pose rather than inventing history");
        buffer.Advance(0.25, 0.2, out sample);
        // 速度调节最多 5%，浮点阈值边缘仍应接近快照正中间。
        Check(sample.Position.x > 0.49f && sample.Position.x < 0.64f, "Position is sampled between two server snapshots");
        Check(Quaternion.Angle(sample.Rotation, Quaternion.identity) < 3f, "Rotation takes shortest arc through 360 degrees");
        double time = buffer.PlaybackTime;
        Check(!buffer.Push(Frame(102, 2f), 0.2, 5f, out _) && !buffer.Push(Frame(101, 2f), 0.2, 5f, out _), "Duplicate and old ticks are rejected");
        Check(buffer.PlaybackTime == time, "Rejected packets cannot rewind playback");
        // 传 0 显式关闭外推，用来保留“只有插值时缓冲耗尽会停在末帧”的基线测试。
        buffer.Advance(10, 0.2, out sample, 0);
        Check(sample.Position.x == 1f && buffer.Starved, "Starvation holds newest pose without extrapolation");
        buffer.Push(Frame(104, 2f), 0.2, 5f, out _);
        buffer.Advance(0.05, 0.2, out sample);
        Check(sample.Position.x > 1f && sample.Position.x < 2f, "New data resumes interpolation after starvation");
        buffer.Push(Frame(106, 20f), 0.2, 5f, out reset);
        Check(reset && buffer.Count == 1, "Teleport clears old path");
        buffer.Push(Frame(160, 21f), 0.2, 5f, out reset);
        Check(reset && Math.Abs(buffer.PlaybackTime - 7.8) < 0.00001, "Long outage establishes a fresh delayed baseline");
        for (uint tick = 162; tick < 300; tick += 2) buffer.Push(Frame(tick, 21f), 0.2, 5f, out _);
        Check(buffer.Count == SnapshotInterpolationBuffer.Capacity, "Buffer capacity is bounded");
        buffer.Advance(0.016, 0.2, out sample);
        Check(buffer.BufferedSeconds < 0.25 && buffer.BufferedSeconds > 0, "Large queued burst catches up to delayed timeline");
        buffer.Clear();
        Check(buffer.Count == 0 && !buffer.Advance(0.1, 0.2, out _), "Pooling reset discards previous entity history");

        InterpolationSnapshot idle = Frame(100, 0f);
        idle.IsPlayer = true;
        InterpolationSnapshot roll = Frame(102, 1f);
        roll.IsPlayer = true;
        roll.IsFiring = true;
        roll.Action = new PlayerActionState { RollTicks = 15, RollDirection = Vector2.right };
        buffer.Push(idle, 0.2, 5f, out _);
        buffer.Push(roll, 0.2, 5f, out _);
        buffer.Advance(0.25, 0.2, out sample);
        Check(!sample.Action.IsRolling && !sample.IsFiring, "Discrete actions do not leak from future snapshot");
        buffer.Advance(0.1, 0.2, out sample);
        Check(sample.Action.IsRolling && sample.IsFiring, "Actions switch when playback reaches their tick");

        // 外推只覆盖短暂丢包：即使很久没有新包，也最多猜 0.15 秒、移动 1 米。
        buffer.Clear();
        buffer.Push(PlayerFrame(100, 0f, 10f), 0.2, 5f, out _);
        buffer.Push(PlayerFrame(102, 1f, 10f), 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(buffer.IsExtrapolating && Math.Abs(buffer.ExtrapolatedSeconds - 0.15) < 0.00001, "Starvation enters bounded extrapolation window");
        Check(sample.Position.x > 1.99f && sample.Position.x < 2.01f, "Extrapolation distance is capped to one metre");
        buffer.Advance(10, 0.2, out InterpolationSnapshot cappedSample);
        Check(cappedSample.Position == sample.Position, "Long outage cannot continue extrapolation forever");

        // 最新权威状态已经停止、死亡或受击时，旧速度都不再可信，必须停在最新位置。
        buffer.Clear();
        buffer.Push(PlayerFrame(100, 0f, 10f), 0.2, 5f, out _);
        buffer.Push(PlayerFrame(102, 1f, 0f), 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(sample.Position.x == 1f, "Stopped player is not extrapolated from stale velocity");
        buffer.Clear();
        InterpolationSnapshot dead = PlayerFrame(102, 1f, 10f);
        dead.Dead = true;
        buffer.Push(PlayerFrame(100, 0f, 10f), 0.2, 5f, out _);
        buffer.Push(dead, 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(sample.Position.x == 1f, "Dead player never extrapolates");
        buffer.Clear();
        InterpolationSnapshot stunned = PlayerFrame(102, 1f, 10f);
        stunned.Action.HitStunTicks = 1;
        buffer.Push(PlayerFrame(100, 0f, 10f), 0.2, 5f, out _);
        buffer.Push(stunned, 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(sample.Position.x == 1f, "Hit-stunned player never extrapolates");

        // 翻滚只能猜到服务器声明的剩余 Tick，不能因为丢包被无限延长。
        buffer.Clear();
        InterpolationSnapshot rolling = PlayerFrame(102, 1f, 0f);
        rolling.Action = new PlayerActionState { RollTicks = 1, RollDirection = Vector2.right };
        buffer.Push(PlayerFrame(100, 0f, 10f), 0.2, 5f, out _);
        buffer.Push(rolling, 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(sample.Position.x > 1.49f && sample.Position.x < 1.51f, "Roll extrapolation respects remaining roll ticks");

        // 敌人/Boss 优先使用服务器随快照发送的速度，并保守地忽略垂直分量。
        buffer.Clear();
        InterpolationSnapshot entityA = Frame(100, 0f);
        entityA.HasVelocity = true;
        entityA.Velocity = new Vector3(4f, 8f, 0f);
        InterpolationSnapshot entityB = entityA;
        entityB.Tick = 102;
        buffer.Push(entityA, 0.2, 5f, out _);
        buffer.Push(entityB, 0.2, 5f, out _);
        buffer.Advance(10, 0.2, out sample);
        Check(sample.Position.x > 0.59f && sample.Position.x < 0.61f && sample.Position.y == 0f, "Entity extrapolation uses authoritative horizontal velocity only");
        InterpolationSnapshot invalid = Frame(104, 0f);
        invalid.Position.x = float.NaN;
        Check(!buffer.Push(invalid, 0.2, 5f, out _), "Invalid floating-point snapshots are rejected");

        float at30 = Simulate(30, out bool monotonic30);
        float at120 = Simulate(120, out bool monotonic120);
        Check(monotonic30 && monotonic120, "Jittered arrivals never move playback backward");
        Check(Mathf.Abs(at30 - at120) < 0.1f, "Timeline playback is consistent across display frame rates");

        GameObject view = new GameObject("Snapshot interpolation test view");
        try
        {
            NetworkTransformInterpolator interpolator = view.AddComponent<NetworkTransformInterpolator>();
            interpolator.Initialize(false);
            interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.zero, CurrentHealth = 100f }, 100);
            interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.right, CurrentHealth = 100f }, 102);
            Check(view.transform.position == Vector3.zero && interpolator.BufferedSnapshotCount == 2, "Receiving next snapshot does not immediately move the model");
            interpolator.ApplyPredictedState(Vector3.right * 2f, 0f, 1f);
            Check(interpolator.BufferedSnapshotCount == 0, "Local prediction does not use delayed remote buffer");
            interpolator.SnapTo(Vector3.right * 3f, 0f, 1f);
            Check(view.transform.position.x == 3f, "Local hard correction remains immediate");
            interpolator.Initialize(false);
            Check(interpolator.BufferedSnapshotCount == 0, "Reinitialization clears the timeline");

            // 恢复包抵达的第一帧保持外推后的画面位置，随后把误差快速、平滑地衰减掉。
            const float isolatedY = 50f;
            interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.up * isolatedY, MoveSpeed = 10f, CurrentHealth = 100f }, 100);
            interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.up * isolatedY + Vector3.right, MoveSpeed = 10f, CurrentHealth = 100f }, 102);
            interpolator.TickRemotePresentation(1f);
            float extrapolatedX = view.transform.position.x;
            Check(interpolator.IsExtrapolating && extrapolatedX > 1.99f && extrapolatedX < 2.01f,
                $"Remote view renders bounded extrapolated pose (actual {extrapolatedX:0.000})");
            interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.up * isolatedY + Vector3.right * 1.2f, MoveSpeed = 2f, CurrentHealth = 100f }, 104);
            interpolator.TickRemotePresentation(0f);
            Check(Mathf.Abs(view.transform.position.x - extrapolatedX) < 0.01f, "Recovery snapshot does not cause an immediate visual snap");
            for (int i = 0; i < 20; i++) interpolator.TickRemotePresentation(0.016f);
            Check(view.transform.position.x < extrapolatedX - 0.2f, "Extrapolation correction converges back to server timeline");

            // 显示层外推也要做静态场景扫掠；这里只验证墙体，角色代理仍由权威状态驱动。
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.name = "Snapshot extrapolation test wall";
                wall.transform.SetPositionAndRotation(new Vector3(1.2f, isolatedY + 1f, 0f), Quaternion.identity);
                wall.transform.localScale = new Vector3(0.1f, 2f, 4f);
                Physics.SyncTransforms();
                interpolator.Initialize(false);
                interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.up * isolatedY, MoveSpeed = 5f, CurrentHealth = 100f }, 200);
                interpolator.ApplyState(new PlayerNetworkState { Position = Vector3.up * isolatedY + Vector3.right * 0.5f, MoveSpeed = 5f, CurrentHealth = 100f }, 202);
                interpolator.TickRemotePresentation(1f);
                Check(view.transform.position.x > 0.5f && view.transform.position.x < 0.9f, "Extrapolated view stops before static wall");
            }
            finally { UnityEngine.Object.DestroyImmediate(wall); }
        }
        finally { UnityEngine.Object.DestroyImmediate(view); }

        GameObject smoothRoot = new GameObject("Local presentation smoothing test");
        GameObject smoothModel = new GameObject("Model");
        try
        {
            smoothModel.transform.SetParent(smoothRoot.transform, false);
            PlayerPresentationDriver driver = smoothRoot.AddComponent<PlayerPresentationDriver>();
            driver.Initialize(true, smoothModel.transform);
            driver.SetPredictedPose(Vector3.right * 0.5f, Quaternion.identity, Vector3.zero, false);
            Check(smoothRoot.transform.position.x == 0.5f && driver.VisualRoot.position.x < 0.01f,
                "Local simulation root advances immediately while VisualRoot preserves the previous frame");
            driver.TickPresentation(1f / 60f);
            Check(driver.VisualRoot.position.x > 0f && driver.VisualRoot.position.x < 0.5f,
                "VisualRoot removes a 20 Hz step continuously at render rate");
            for (int i = 0; i < 30; i++) driver.TickPresentation(1f / 60f);
            Check(Mathf.Abs(driver.VisualRoot.position.x - 0.5f) < 0.002f && driver.CameraAnchor.IsChildOf(driver.VisualRoot),
                "VisualRoot converges and CameraAnchor follows the smoothed pose");
            driver.SetPredictedPose(Vector3.right * 2f, Quaternion.identity, Vector3.zero, false);
            Check(Mathf.Abs(driver.VisualRoot.position.x - 2f) < 0.001f, "Corrections over one metre hard-snap");
        }
        finally { UnityEngine.Object.DestroyImmediate(smoothRoot); }

        GameObject controllerRoot = new GameObject("Network roll IK test");
        GameObject defaultSocketParent = new GameObject("Weapon socket parent");
        GameObject weapon = new GameObject("Weapon");
        GameObject weaponAimPivot = new GameObject("Weapon aim pivot");
        GameObject rightHand = new GameObject("Right hand");
        GameObject rigObject = new GameObject("AimRig");
        try
        {
            defaultSocketParent.transform.SetParent(controllerRoot.transform, false);
            weapon.transform.SetParent(defaultSocketParent.transform, false);
            weaponAimPivot.transform.SetParent(weapon.transform, false);
            rightHand.transform.SetParent(controllerRoot.transform, false);
            rigObject.transform.SetParent(controllerRoot.transform, false);
            Rig rig = rigObject.AddComponent<Rig>();
            rig.weight = 1f;
            GrayboxPlayerController controller = controllerRoot.AddComponent<GrayboxPlayerController>();
            SetField(controller, "aimRig", rig);
            SetField(controller, "weaponSocket", weapon.transform);
            SetField(controller, "rightHandBone", rightHand.transform);
            SetField(controller, "weaponSocketDefaultParent", defaultSocketParent.transform);
            SetField(controller, "weaponSocketDefaultLocalPosition", Vector3.zero);
            SetField(controller, "weaponSocketDefaultLocalRotation", Quaternion.identity);
            SetField(controller, "weaponSocketDefaultLocalScale", Vector3.one);
            SetField(controller, "weaponAimPivot", weaponAimPivot.transform);
            SetField(controller, "weaponAimPivotDefaultLocalRotation", Quaternion.identity);
            controller.ConfigureNetworkView(true);
            weaponAimPivot.transform.localRotation = Quaternion.Euler(0f, 73f, 0f);
            controller.ApplyNetworkMotion(new PlayerActionState { RollTicks = 15, RollSequence = 1, RollStartTick = 10 }, false, false);
            Check(rig.weight == 0f && weapon.transform.parent == rightHand.transform,
                "Network roll immediately disables AimRig and hands the weapon to the animation");
            controller.ApplyNetworkMotion(new PlayerActionState { RollSequence = 1, RollStartTick = 10 }, false, false);
            Check(weapon.transform.parent == defaultSocketParent.transform && weaponAimPivot.transform.localRotation == Quaternion.identity,
                "Network roll completion restores the weapon socket and aim pivot");
            rig.weight = 1f;
            controller.ApplyNetworkMotion(new PlayerActionState { HitSequence = 1, HitStunTicks = 3 }, false, false);
            Check(rig.weight == 0f && weapon.transform.parent == rightHand.transform,
                "Network hit immediately disables AimRig and hands the weapon to the animation");
            controller.ApplyNetworkMotion(new PlayerActionState { HitSequence = 1 }, false, false);
            Check(weapon.transform.parent == defaultSocketParent.transform,
                "Network hit completion restores the weapon socket");
        }
        finally { UnityEngine.Object.DestroyImmediate(controllerRoot); }
        string result = "PASS: " + results.Count + " snapshot interpolation checks\n" + string.Join("\n", results);
        File.WriteAllText("Temp/NetworkSnapshotInterpolationChecks.txt", result);
        return result;
    }

    private static InterpolationSnapshot Frame(uint tick, float x, float yaw = 0f)
        => new InterpolationSnapshot { Tick = tick, Position = Vector3.right * x, Rotation = Quaternion.Euler(0f, yaw, 0f) };

    private static InterpolationSnapshot PlayerFrame(uint tick, float x, float moveSpeed)
        => new InterpolationSnapshot { Tick = tick, Position = Vector3.right * x, Rotation = Quaternion.identity, IsPlayer = true, MoveSpeed = moveSpeed };

    private static float Simulate(int fps, out bool monotonic)
    {
        SnapshotInterpolationBuffer buffer = new SnapshotInterpolationBuffer();
        buffer.Push(Frame(0, 0f), 0.2, 5f, out _);
        uint nextTick = 2;
        monotonic = true;
        InterpolationSnapshot sample = default;
        for (int frame = 1; frame <= fps * 4; frame++)
        {
            double now = frame / (double)fps;
            while (nextTick / 20.0 + (nextTick % 6) * 0.005 <= now)
            {
                buffer.Push(Frame(nextTick, nextTick / 20f), 0.2, 5f, out _);
                nextTick += 2;
            }
            double before = buffer.PlaybackTime;
            buffer.Advance(1.0 / fps, 0.2, out sample);
            monotonic &= buffer.PlaybackTime >= before;
        }
        return sample.Position.x;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }
}
