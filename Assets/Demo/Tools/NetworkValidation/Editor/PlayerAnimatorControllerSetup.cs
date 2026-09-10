using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerAnimatorControllerSetup
{
    private const string ControllerPath = "Assets/Models/player/Player Animator Controller.controller";

    static PlayerAnimatorControllerSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("Tools/Network Validation/Ensure Player Animator Timing")]
    public static void Ensure()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0) return;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState roll = FindState(machine, "Roll");
        AnimatorState hit = FindState(machine, "Hit");
        AnimatorState locomotion = FindState(machine, "Locomotion");
        if (roll == null || hit == null || locomotion == null) return;

        bool changed = false;
        changed |= ConfigureEntry(machine, roll, 0.05f);
        changed |= ConfigureEntry(machine, hit, 0.04f);

        AnimatorStateTransition rollExit = Array.Find(roll.transitions, transition => transition.destinationState == locomotion);
        if (rollExit != null)
        {
            changed |= SetFloat(rollExit.duration, 0.08f, value => rollExit.duration = value);
            changed |= SetFloat(rollExit.exitTime, 1f, value => rollExit.exitTime = value);
            if (!rollExit.hasFixedDuration) { rollExit.hasFixedDuration = true; changed = true; }
            if (!rollExit.hasExitTime) { rollExit.hasExitTime = true; changed = true; }
        }

        const float rollDuration = 0.75f;
        float requiredSpeed = roll.motion != null ? roll.motion.averageDuration / rollDuration : roll.speed;
        changed |= SetFloat(roll.speed, requiredSpeed, value => roll.speed = value);
        if (!changed) return;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("已校正玩家 Animator：Roll 0.75s（进入 0.05s / 退出 0.08s），Hit 进入 0.04s。");
    }

    private static bool ConfigureEntry(AnimatorStateMachine machine, AnimatorState state, float duration)
    {
        AnimatorStateTransition transition = Array.Find(machine.anyStateTransitions, value => value.destinationState == state);
        if (transition == null) return false;
        bool changed = SetFloat(transition.duration, duration, value => transition.duration = value);
        if (!transition.hasFixedDuration) { transition.hasFixedDuration = true; changed = true; }
        return changed;
    }

    private static bool SetFloat(float current, float desired, Action<float> setter)
    {
        if (Mathf.Abs(current - desired) < 0.0001f) return false;
        setter(desired);
        return true;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
            if (child.state != null && child.state.name == name) return child.state;
        return null;
    }
}
