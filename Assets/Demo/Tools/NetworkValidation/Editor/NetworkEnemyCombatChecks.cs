using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;

public static class NetworkEnemyCombatChecks
{
    [MenuItem("Tools/Network Validation/Run Enemy Combat Checks")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在非播放状态运行敌人检查。");
        NetworkEnemyPrefabSetup.EnsureAsset();
        List<string> passed = new List<string>();
        NetworkEnemyPrefabs assets = Resources.Load<NetworkEnemyPrefabs>("NetworkEnemyPrefabs");
        Check(assets != null && assets.Melee != null && assets.Ranged != null && assets.Boss != null && assets.Projectile != null,
            "All offline enemy prefabs are assigned", passed);
        ValidateAnimationTiming(passed);

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            GameObject owner = new GameObject("EnemyCatalogCheck");
            NetworkPrefabCatalog catalog = owner.AddComponent<NetworkPrefabCatalog>();
            catalog.Initialize();
            ValidateSpawn(catalog, NetworkPrefabCatalog.TestEnemyPrefabId, "Enemy_Melee", true, passed);
            ValidateSpawn(catalog, NetworkPrefabCatalog.RangedEnemyPrefabId, "Enemy_Range", true, passed);
            ValidateSpawn(catalog, NetworkPrefabCatalog.BossPrefabId, "Boss", true, passed);
            ValidateSpawn(catalog, NetworkPrefabCatalog.EnemyProjectilePrefabId, assets.Projectile.name, false, passed);

            BattleEventMessage attack = new BattleEventMessage
            {
                EventType = BattleEventType.EnemyAttackStarted, SourceEntityId = 2001, TargetEntityId = 1001,
                SkillSlot = 5, Position = Vector3.one, Direction = Vector3.forward, Range = 12f, Duration = 0.8f
            };
            BattleEventMessage copy = NetworkProtocol.DeserializeBattleEvent(NetworkProtocol.Serialize(attack));
            Check(copy.EventType == attack.EventType && copy.SourceEntityId == attack.SourceEntityId &&
                copy.TargetEntityId == attack.TargetEntityId && copy.SkillSlot == attack.SkillSlot && copy.Range == attack.Range,
                "Enemy attack protocol round-trips", passed);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetPositionAndRotation(new Vector3(0f, 1f, 2f), Quaternion.identity);
            wall.transform.localScale = new Vector3(2f, 2f, 0.5f);
            Physics.SyncTransforms();
            Check(!ServerEntityRegistry.HasAttackSight(Vector3.zero, Vector3.forward * 4f), "Walls block enemy attacks", passed);
            wall.transform.position = Vector3.right * 10f;
            Physics.SyncTransforms();
            Check(ServerEntityRegistry.HasAttackSight(Vector3.zero, Vector3.forward * 4f), "Clear line permits enemy attacks", passed);

            return "PASS: " + passed.Count + " enemy prefab/combat checks\n" + string.Join("\n", passed);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void ValidateSpawn(NetworkPrefabCatalog catalog, int prefabId, string expectedName, bool character,
        List<string> passed)
    {
        GameObject instance = catalog.Spawn(new EntitySpawnMessage
        {
            EntityId = prefabId + 2000, PrefabId = prefabId, Position = Vector3.zero, Rotation = Quaternion.identity
        });
        Check(instance != null && instance.name.Contains(expectedName), $"Prefab {prefabId} uses {expectedName}", passed);
        Check(Array.TrueForAll(instance.GetComponentsInChildren<Collider>(true), value => !value.enabled),
            $"Prefab {prefabId} client colliders are disabled", passed);
        Check(Array.TrueForAll(instance.GetComponentsInChildren<EnemyAIController>(true), value => !value.enabled) &&
            Array.TrueForAll(instance.GetComponentsInChildren<BossController>(true), value => !value.enabled) &&
            Array.TrueForAll(instance.GetComponentsInChildren<EnemyHomingProjectile>(true), value => !value.enabled),
            $"Prefab {prefabId} offline combat behaviours are disabled", passed);
        if (character) Check(instance.GetComponentInChildren<Animator>(true) != null, $"Prefab {prefabId} retains its Animator", passed);
        catalog.Release(instance);
    }

    private static void Check(bool condition, string description, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException(description);
        passed.Add(description);
    }

    private static void ValidateAnimationTiming(List<string> passed)
    {
        AnimatorController melee = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/小怪/MeleeEnemyAnimatorController.controller");
        AnimatorController ranged = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/小怪/RangedEnemyAnimatorController.controller");
        AnimatorController boss = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Boss/BossAnimator.controller");
        AnimatorController player = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Models/player/Player Animator Controller.controller");
        Check(Mathf.Abs(Constant(typeof(ServerEntityRegistry), "MeleeHitTime") - EventTime(melee, "Attack", "AnimationEvent_AttackHit")) < 0.002f,
            "Melee damage matches the prefab hit frame", passed);
        Check(Mathf.Abs(Constant(typeof(ServerEntityRegistry), "RangedHitTime") - EventTime(ranged, "Attack", "AnimationEvent_FireHomingProjectile")) < 0.002f,
            "Ranged projectile matches the prefab fire frame", passed);
        Check(Mathf.Abs(Constant(typeof(ServerEntityRegistry), "BossMeleeHitTime") - EventTime(boss, "Attack1", "AnimationEvent_AttackHit")) < 0.002f,
            "Boss melee damage matches the prefab hit frame", passed);
        float jump = EventTime(boss, "Slam", "AnimationEvent_SlamJump");
        float hit = EventTime(boss, "Slam", "AnimationEvent_SlamHit");
        Check(Mathf.Abs(Constant(typeof(ServerEntityRegistry), "BossSlamJumpTime") - jump) < 0.002f &&
            Mathf.Abs(Constant(typeof(ServerEntityRegistry), "BossSlamTravelTime") - (hit - jump)) < 0.002f,
            "Boss slam movement matches its jump and impact frames", passed);
        AnimatorState roll = State(player, "Roll");
        AnimatorStateTransition entry = Array.Find(player.layers[0].stateMachine.anyStateTransitions, value => value.destinationState == roll);
        AnimatorStateTransition exit = Array.Find(roll.transitions,
            value => value.destinationState != null && value.destinationState.name == "Locomotion");
        float gameplayDuration = PlayerMovementSimulation.RollDurationTicks * PlayerMovementSimulation.TickDeltaTime;
        Check(entry != null && exit != null && Mathf.Abs(gameplayDuration - roll.motion.averageDuration / roll.speed) < 0.03f &&
            Mathf.Abs(entry.duration - 0.05f) < 0.001f && Mathf.Abs(exit.duration - 0.08f) < 0.001f &&
            Mathf.Abs(exit.exitTime - 1f) < 0.001f,
            "Roll clip duration and entry/exit transitions match deterministic gameplay", passed);
        ValidateExtractedRollImport("Assets/Animations/Ch44_nonPBR@Running Dive Roll.fbx", passed);
        ValidateInPlaceImport("Assets/Models/player/Ch44_nonPBR@Hit Reaction.fbx", passed);
        ValidateInPlaceImport("Assets/Animations/Ch44_nonPBR@Falling Back Death.fbx", passed);
    }

    private static void ValidateInPlaceImport(string path, List<string> passed)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        ModelImporterClipAnimation[] clips = importer != null ? importer.clipAnimations : null;
        if (importer != null && (clips == null || clips.Length == 0)) clips = importer.defaultClipAnimations;
        Check(clips != null && clips.Length > 0 && Array.TrueForAll(clips,
            clip => clip.lockRootRotation && clip.lockRootHeightY && clip.lockRootPositionXZ),
            $"{System.IO.Path.GetFileNameWithoutExtension(path)} is imported in-place", passed);
    }

    private static void ValidateExtractedRollImport(string path, List<string> passed)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        ModelImporterClipAnimation[] clips = importer != null ? importer.clipAnimations : null;
        if (importer != null && (clips == null || clips.Length == 0)) clips = importer.defaultClipAnimations;
        Check(clips != null && clips.Length > 0 && Array.TrueForAll(clips,
            clip => clip.lockRootRotation && clip.lockRootHeightY && !clip.lockRootPositionXZ),
            $"{System.IO.Path.GetFileNameWithoutExtension(path)} extracts XZ motion for relay consumption", passed);
    }

    private static float EventTime(AnimatorController controller, string stateName, string eventName)
    {
        AnimatorState state = State(controller, stateName);
        AnimationClip clip = state.motion as AnimationClip;
        AnimationEvent animationEvent = Array.Find(AnimationUtility.GetAnimationEvents(clip), value => value.functionName == eventName);
        return animationEvent.time / state.speed;
    }

    private static AnimatorState State(AnimatorController controller, string name)
    {
        return Array.Find(controller.layers[0].stateMachine.states, value => value.state.name == name).state;
    }

    private static float Constant(Type type, string name)
    {
        return Convert.ToSingle(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue());
    }
}
