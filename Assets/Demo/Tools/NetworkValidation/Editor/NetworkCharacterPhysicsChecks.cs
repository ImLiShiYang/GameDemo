using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>实际 Unity CharacterController/Physics 验证；只创建并关闭临时测试场景。</summary>
public static class NetworkCharacterPhysicsChecks
{
    private static readonly Vector3 Origin = new Vector3(10000f, 1000f, 10000f);
    private static readonly List<string> passed = new List<string>();
    private static Scene testScene;

    [MenuItem("Tools/Network Validation/Run Character Physics Checks")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在非播放状态运行物理检查。");
        Scene previous = SceneManager.GetActiveScene();
        passed.Clear();
        testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));
            TestGroundWallsAndRoll();
            TestCharactersAndReplay();
            TestStepsSlopesAndSpawn();
            TestPredictionPipelines();
            TestLoadedSceneSpawns(previous);
            string result = "PASS: " + passed.Count + " native character physics checks\n" + string.Join("\n", passed);
            File.WriteAllText("Temp/NetworkCharacterPhysicsChecks.txt", result);
            return result;
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/NetworkCharacterPhysicsChecks.txt", "FAIL after " + passed.Count + " checks\n" + string.Join("\n", passed) + "\n" + exception);
            throw;
        }
        finally
        {
            EditorSceneManager.CloseScene(testScene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void TestGroundWallsAndRoll()
    {
        NetworkCharacterWorld world = World();
        NetworkCharacterMotor player = world.Create(1, 1, At(0f, 3f, 0f), true);
        Advance(player, Vector3.zero, 80);
        Check(player.State.Grounded && Mathf.Abs(player.State.Position.y - Origin.y) < 0.12f, "Gravity lands on ground");
        Check(Mathf.Abs(player.State.VerticalVelocity + 2f) < 0.01f, "Grounded velocity is retained");
        GameObject wall = Box("Wall", new Vector3(3f, 2f, 0f), new Vector3(0.5f, 4f, 12f));
        Advance(player, Vector3.right * 0.16f, 80);
        Check(player.State.Position.x < Origin.x + 2.4f, "Walking cannot pass wall");
        float beforeSlide = player.State.Position.z;
        Advance(player, new Vector3(0.1f, 0f, 0.1f), 20);
        Check(player.State.Position.z > beforeSlide + 1f && player.State.Position.x < Origin.x + 2.4f, "Oblique wall contact slides");
        player.Restore(new CharacterMotorState { Position = At(1.5f, 0.02f, 0f), Grounded = true, VerticalVelocity = -2f });
        PlayerActionState action = default;
        float yaw = 0f;
        for (int tick = 0; tick < PlayerMovementSimulation.RollDurationTicks; tick++)
        {
            Vector3 desired = player.State.Position;
            PlayerMovementSimulation.Step(ref desired, ref yaw, ref action, Vector2.right, Vector2.up,
                tick == 0 ? ClientInputButtons.Roll : ClientInputButtons.None, true);
            player.Step(desired - player.State.Position, 0.05f);
        }
        Check(action.IsRolling && action.IsInvincible && player.State.Position.x < Origin.x + 2.4f, "Blocked roll retains duration and invulnerability");
        Check(NetworkCharacterWorld.IsCharacterCollider(player.Controller), "Movement body is excluded from projectile world hits");
        GameObject corner = Box("Corner", new Vector3(0f, 2f, 3f), new Vector3(12f, 4f, 0.5f));
        Advance(player, new Vector3(0.2f, 0f, 0.2f), 80);
        Check(player.State.Position.x < Origin.x + 2.4f && player.State.Position.z < Origin.z + 2.4f, "Sustained diagonal movement cannot escape wall corner");
        UnityEngine.Object.DestroyImmediate(corner);
        UnityEngine.Object.DestroyImmediate(wall);
        UnityEngine.Object.DestroyImmediate(world.gameObject);
    }

    private static void TestCharactersAndReplay()
    {
        NetworkCharacterWorld world = World();
        NetworkCharacterMotor player = world.Create(1, 1, At(0f, 0.02f, 0f), true);
        NetworkCharacterMotor teammate = world.Create(2, 1, At(2f, 0.02f, 0f), true);
        Vector3 otherStart = teammate.State.Position;
        Advance(player, Vector3.right * 0.267f, 30);
        Check(Vector3.Distance(player.State.Position, teammate.State.Position) >= 0.99f, "Rolling displacement cannot pass teammate");
        Check(teammate.State.Position == otherStart, "Contact does not push stationary teammate");
        Check(player.State.Position.y < Origin.y + 0.12f, "Character contact cannot become a stair");
        for (int tick = 0; tick < 30; tick++)
        {
            player.Step(Vector3.right * 0.267f, 0.05f);
            teammate.Step(Vector3.left * 0.267f, 0.05f);
        }
        Check(!world.HasActorOverlap(player, player.State.Position), "Opposing characters never penetrate");
        world.Remove(2);
        Advance(player, Vector3.right * 0.2f, 10);
        Check(player.State.Position.x > Origin.x + 2.5f, "Despawn immediately removes blocking");
        player.Restore(new CharacterMotorState { Position = At(0f, 0.02f, 0f), Grounded = true, VerticalVelocity = -2f });
        world.ApplyProxy(10, 10, At(2f, 0.02f, 0f), true);
        world.RefreshContext();
        CharacterCollisionPose[] history = world.LatestContext;
        CharacterMotorState baseline = player.State;
        using (world.UseContext(history)) Advance(player, Vector3.right * 0.15f, 20);
        Vector3 predicted = player.State.Position;
        world.ApplyProxy(10, 10, At(12f, 0.02f, 0f), true);
        world.RefreshContext();
        player.Restore(baseline);
        using (world.UseContext(history)) Advance(player, Vector3.right * 0.15f, 20);
        Check(Vector3.Distance(predicted, player.State.Position) < 0.02f, "Replay uses saved actor poses, not current remote pose");
        Advance(player, Vector3.right * 0.15f, 20);
        Check(player.State.Position.x > Origin.x + 3f, "Replay scope restores current collision context");
        world.SetBlocking(10, false);
        player.Restore(baseline);
        Advance(player, Vector3.right * 0.2f, 70);
        Check(player.State.Position.x > Origin.x + 13f, "Dead proxy does not block");
        player.Restore(baseline);
        world.Remove(10);
        using (world.UseContext(history)) Advance(player, Vector3.right * 0.15f, 20);
        Check(Vector3.Distance(predicted, player.State.Position) < 0.02f, "Despawn does not invalidate retained replay history");
        try { using (world.UseContext(history)) throw new InvalidOperationException("scope test"); }
        catch (InvalidOperationException) { }
        Advance(player, Vector3.right * 0.2f, 20);
        Check(player.State.Position.x > Origin.x + 3f, "Replay exception restores context");
        UnityEngine.Object.DestroyImmediate(world.gameObject);
    }

    private static void TestStepsSlopesAndSpawn()
    {
        NetworkCharacterWorld world = World();
        NetworkCharacterMotor player = world.Create(1, 1, At(-10f, 0.02f, 0f), true);
        GameObject step = Box("Step", new Vector3(-7f, 0.1f, 0f), new Vector3(3f, 0.2f, 4f));
        Advance(player, Vector3.right * 0.1f, 35);
        Check(player.State.Position.x > Origin.x - 7f && player.State.Position.y > Origin.y + 0.1f, "Climbs 0.2m step");
        Advance(player, Vector3.right * 0.1f, 45);
        Check(Mathf.Abs(player.State.Position.y - Origin.y) < 0.12f, "Leaving step returns to ground");
        UnityEngine.Object.DestroyImmediate(step);
        GameObject highStep = Box("HighStep", new Vector3(-7f, 0.4f, 0f), new Vector3(3f, 0.8f, 4f));
        player.Restore(new CharacterMotorState { Position = At(-10f, 0.02f, 0f) });
        Advance(player, Vector3.right * 0.1f, 50);
        Check(player.State.Position.x < Origin.x - 8.7f, "Cannot climb 0.8m step");
        UnityEngine.Object.DestroyImmediate(highStep);
        GameObject slope = Box("Slope", new Vector3(-7f, 0.65f, 0f), new Vector3(6f, 0.2f, 4f));
        slope.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
        Physics.SyncTransforms();
        player.Restore(new CharacterMotorState { Position = At(-11f, 0.02f, 0f) });
        Advance(player, Vector3.right * 0.1f, 50);
        Check(player.State.Position.x > Origin.x - 8f && player.State.Position.y > Origin.y + 0.3f, "Climbs walkable 20 degree slope");
        UnityEngine.Object.DestroyImmediate(slope);
        GameObject steepSlope = Box("SteepSlope", new Vector3(-7f, 2.4f, 0f), new Vector3(6f, 0.2f, 4f));
        steepSlope.transform.rotation = Quaternion.Euler(0f, 0f, 60f);
        Physics.SyncTransforms();
        player.Restore(new CharacterMotorState { Position = At(-11f, 0.02f, 0f) });
        Advance(player, Vector3.right * 0.1f, 80);
        Check(player.State.Position.y < Origin.y + 1f && player.State.Position.x < Origin.x - 7f, "Cannot climb 60 degree slope");
        UnityEngine.Object.DestroyImmediate(steepSlope);
        Check(world.TryFindSpawn(100, At(10f, 2f, 10f), out Vector3 spawn), "Finds grounded Boss spawn");
        NetworkCharacterMotor boss = world.Create(100, 100, spawn, true);
        Check(world.TryFindSpawn(1, spawn, out Vector3 alternative) && Vector3.Distance(spawn, alternative) > 1.4f, "Occupied spawn chooses nearby free position");
        Check(!world.TryFindSpawn(1, At(100f, 0f, 100f), out _), "No ground defers spawn");
        player.Restore(new CharacterMotorState { Position = spawn - Vector3.right * 3f });
        Advance(player, Vector3.right * 0.267f, 30);
        Check(Vector3.Distance(player.State.Position, boss.State.Position) >= 1.39f, "Boss uses larger shared collision shape");
        UnityEngine.Object.DestroyImmediate(world.gameObject);
    }

    private static void TestPredictionPipelines()
    {
        // 三套互不接触的测试区域模拟服务器和两个客户端；不改变全局 NetworkRuntime 身份。
        NetworkCharacterWorld serverWorld = World();
        NetworkCharacterMotor[] authority =
        {
            serverWorld.Create(1001, 1, At(-3f, 0.02f, 20f), true),
            serverWorld.Create(1002, 1, At(3f, 0.02f, 20f), true)
        };
        ClientPlayerPrediction[] predictions = new ClientPlayerPrediction[2];
        NetworkCharacterWorld[] worlds = new NetworkCharacterWorld[2];
        GameObject[] views = new GameObject[2];
        PlayerActionState[] actions = new PlayerActionState[2];
        float[] headings = new float[2];
        Vector3[] offsets = { Vector3.back * 20f, Vector3.back * 40f };
        List<DelayedSnapshot> deliveries = new List<DelayedSnapshot>();
        int previousDelivery = 0;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                worlds[i] = World();
                views[i] = new GameObject("Prediction View " + i);
                SceneManager.MoveGameObjectToScene(views[i], testScene);
                views[i].transform.position = authority[i].State.Position + offsets[i];
                NetworkTransformInterpolator presentation = views[i].AddComponent<NetworkTransformInterpolator>();
                presentation.Initialize(false);
                predictions[i] = worlds[i].gameObject.AddComponent<ClientPlayerPrediction>();
                predictions[i].Initialize(views[i].transform, presentation, 1001 + i);
                Deliver(predictions[i], worlds[i], Snapshot(authority, actions, headings, 0, offsets[i]), 1001 + i);
            }
            for (uint tick = 1; tick <= 140; tick++)
            {
                for (int i = 0; i < 2; i++)
                {
                    float direction = tick <= 60 ? (i == 0 ? 1f : -1f) : tick <= 85 ? (i == 0 ? -1f : 1f) : 0f;
                    ClientInputMessage input = new ClientInputMessage
                    { Sequence = tick, ClientTick = tick, Horizontal = direction, AimZ = 1f, Buttons = tick == 10 ? ClientInputButtons.Roll : ClientInputButtons.None };
                    predictions[i].Predict(input, true);
                    Vector3 desired = authority[i].State.Position;
                    PlayerMovementSimulation.Step(ref desired, ref headings[i], ref actions[i], new Vector2(direction, 0f), Vector2.up, input.Buttons, true);
                    authority[i].Step(desired - authority[i].State.Position, 0.05f);
                }
                if (tick % 2 == 0)
                {
                    // TCP 顺序不变，延迟在 100-250ms 之间波动。
                    previousDelivery = Mathf.Max(previousDelivery, (int)tick + 2 + (int)(tick % 4));
                    for (int i = 0; i < 2; i++) deliveries.Add(new DelayedSnapshot
                    { Tick = previousDelivery, Client = i, State = Snapshot(authority, actions, headings, tick, offsets[i]) });
                }
                foreach (DelayedSnapshot delivery in deliveries)
                    if (delivery.Tick == tick) Deliver(predictions[delivery.Client], worlds[delivery.Client], delivery.State, 1001 + delivery.Client);
            }
            foreach (DelayedSnapshot delivery in deliveries)
                if (delivery.Tick > 140) Deliver(predictions[delivery.Client], worlds[delivery.Client], delivery.State, 1001 + delivery.Client);
            for (int i = 0; i < 2; i++)
            {
                Check(Vector3.Distance(predictions[i].MotorState.Position - offsets[i], authority[i].State.Position) < 0.02f && predictions[i].PendingInputCount == 0,
                    "Client " + (i + 1) + " converges after delayed/jittered contact snapshots");
                Vector3 logical = predictions[i].MotorState.Position;
                views[i].transform.position += Vector3.right * 5f;
                predictions[i].Predict(new ClientInputMessage { Sequence = 141, AimZ = 1f }, true);
                Check(Vector3.Distance(logical, predictions[i].MotorState.Position) < 0.02f, "Presentation transform never drives client " + (i + 1) + " collision");
            }

            ClientPlayerPrediction prediction = predictions[0];
            for (uint sequence = 142; sequence <= 290; sequence++)
                prediction.Predict(new ClientInputMessage { Sequence = sequence, AimZ = 1f }, true);
            WorldSnapshotMessage overflow = Snapshot(authority, actions, headings, 142, offsets[0]);
            Deliver(prediction, worlds[0], overflow, 1001);
            Check(prediction.PendingInputCount == 0, "Overflow falls back to complete authoritative motor state");
            for (uint sequence = 291; sequence <= 295; sequence++)
                prediction.Predict(new ClientInputMessage { Sequence = sequence, AimZ = 1f }, true);
            Deliver(prediction, worlds[0], Snapshot(authority, actions, headings, 180, offsets[0]), 1001);
            Check(prediction.PendingInputCount == 115, "Prediction recovers once acknowledgment catches retained history");
            Deliver(prediction, worlds[0], Snapshot(authority, actions, headings, 295, offsets[0]), 1001);
            Check(prediction.PendingInputCount == 0, "Recovered history drains completely");
        }
        finally
        {
            foreach (NetworkCharacterWorld world in worlds) if (world != null) UnityEngine.Object.DestroyImmediate(world.gameObject);
            foreach (GameObject view in views) if (view != null) UnityEngine.Object.DestroyImmediate(view);
            UnityEngine.Object.DestroyImmediate(serverWorld.gameObject);
        }
    }

    private sealed class DelayedSnapshot { public int Tick; public int Client; public WorldSnapshotMessage State; }
    private static WorldSnapshotMessage Snapshot(NetworkCharacterMotor[] motors, PlayerActionState[] actions, float[] headings, uint sequence, Vector3 offset)
    {
        WorldSnapshotMessage snapshot = new WorldSnapshotMessage { ServerTick = sequence };
        for (int i = 0; i < motors.Length; i++) snapshot.Players.Add(new PlayerNetworkState
        {
            EntityId = 1001 + i, OwnerPlayerId = i + 1, Position = motors[i].State.Position + offset,
            RotationY = headings[i], Action = actions[i], VerticalVelocity = motors[i].State.VerticalVelocity,
            Grounded = motors[i].State.Grounded, CurrentHealth = 100f, LastProcessedInputSequence = sequence
        });
        return NetworkProtocol.DeserializeWorldSnapshot(NetworkProtocol.Serialize(snapshot));
    }
    private static void Deliver(ClientPlayerPrediction prediction, NetworkCharacterWorld world, WorldSnapshotMessage snapshot, int localId)
    {
        world.ApplySnapshot(snapshot, localId);
        foreach (PlayerNetworkState state in snapshot.Players) if (state.EntityId == localId) prediction.Reconcile(state);
    }

    private static void TestLoadedSceneSpawns(Scene loadedScene)
    {
        GrayboxPlayerController player = null;
        foreach (GrayboxPlayerController candidate in UnityEngine.Object.FindObjectsOfType<GrayboxPlayerController>(true))
            if (candidate.gameObject.scene == loadedScene) { player = candidate; break; }
        if (player == null) return;
        NetworkCharacterWorld world = World();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 requested = player.transform.position + Vector3.right * (i == 0 ? -1.25f : 1.25f);
                Check(world.TryFindSpawn(1, requested, out Vector3 spawn), "Loaded scene has valid spawn for player " + (i + 1));
                world.Create(1001 + i, 1, spawn, true);
            }
            Check(world.TryFindSpawn(100, player.transform.position + Vector3.forward * 8f, out _), "Loaded scene has valid Boss spawn");
        }
        finally { UnityEngine.Object.DestroyImmediate(world.gameObject); }
    }

    private static NetworkCharacterWorld World()
    {
        GameObject root = new GameObject("Character Physics Test World");
        SceneManager.MoveGameObjectToScene(root, testScene);
        return root.AddComponent<NetworkCharacterWorld>();
    }
    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        GameObject box = new GameObject(name);
        SceneManager.MoveGameObjectToScene(box, testScene);
        box.transform.position = Origin + position;
        box.AddComponent<BoxCollider>().size = size;
        Physics.SyncTransforms();
        return box;
    }
    private static Vector3 At(float x, float y, float z) => Origin + new Vector3(x, y, z);
    private static void Advance(NetworkCharacterMotor motor, Vector3 displacement, int count)
    { for (int i = 0; i < count; i++) motor.Step(displacement, 0.05f); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed.Add(message);
    }
}
