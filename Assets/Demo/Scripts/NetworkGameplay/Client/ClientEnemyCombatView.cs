using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>可靠攻击事件驱动原预制体动画和预警，动画事件不参与网络伤害。</summary>
public sealed class ClientEnemyCombatView : MonoBehaviour
{
    private Animator animator;
    private DecalProjector chargeWarning;
    private DecalProjector slamWarning;
    private byte attack;
    private float windupEnd;
    private bool serverAttackActive;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        foreach (DecalProjector decal in GetComponentsInChildren<DecalProjector>(true))
        {
            if (decal.name.Contains("Charge")) chargeWarning = decal;
            if (decal.name.Contains("Slam")) slamWarning = decal;
        }
    }

    public void PlayAttack(BattleEventMessage message)
    {
        ResetPresentation();
        attack = message.SkillSlot;
        serverAttackActive = true;
        windupEnd = Time.unscaledTime + message.Duration;
        SetParameter("Speed", 0f);
        if (attack == 3 || attack == 4) Trigger("Attack");
        if (attack == 6) Trigger("Slam");
        DecalProjector warning = attack == 5 ? chargeWarning : attack == 6 ? slamWarning : null;
        if (warning == null) return;
        Vector3 position = message.Position;
        if (attack == 5) position += message.Direction * (message.Range * 0.5f);
        warning.transform.SetPositionAndRotation(position + Vector3.up, Quaternion.Euler(90f, Quaternion.LookRotation(message.Direction).eulerAngles.y, 0f));
        warning.size = attack == 5 ? new Vector3(3f, message.Range, 4f) : new Vector3(message.Range * 2f, message.Range * 2f, 4f);
        warning.pivot = Vector3.zero;
        warning.enabled = true;
    }

    public void FinishAttack()
    {
        serverAttackActive = false;
        if (chargeWarning != null) chargeWarning.enabled = false;
        if (slamWarning != null) slamWarning.enabled = false;
        SetParameter("IsCharging", false);
    }

    public void ResetPresentation()
    {
        attack = 0;
        FinishAttack();
    }

    public bool IsAttackAnimationPlaying()
    {
        if (attack == 0) return false;
        if (serverAttackActive) return true;
        if (animator == null) return false;
        string stateName = attack == 6 ? "Slam" : attack == 5 ? "Run" : attack == 3 || attack == 4 ?
            (GetComponent<NetworkEntity>()?.EntityType == NetworkEntityType.Boss ? "Attack1" : "Attack") : string.Empty;
        return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) ||
            animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName);
    }

    private void Update()
    {
        if (serverAttackActive && attack == 5 && Time.unscaledTime >= windupEnd)
        {
            if (chargeWarning != null) chargeWarning.enabled = false;
            SetParameter("Speed", 1f);
            bool running = animator != null && (animator.GetCurrentAnimatorStateInfo(0).IsName("Run") ||
                animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Run"));
            if (animator != null && !running) animator.CrossFadeInFixedTime("Run", 0.08f);
        }
        if (!serverAttackActive && !IsAttackAnimationPlaying()) attack = 0;
    }

    private void OnDisable() { ResetPresentation(); }

    private void Trigger(string name)
    {
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == name && parameter.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger(name);
    }

    private void SetParameter(string name, bool value)
    {
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == name && parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(name, value);
    }

    private void SetParameter(string name, float value)
    {
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == name && parameter.type == AnimatorControllerParameterType.Float) animator.SetFloat(name, value);
    }
}
