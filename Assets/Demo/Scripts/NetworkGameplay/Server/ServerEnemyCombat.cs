using UnityEngine;

public sealed partial class ServerEntityRegistry
{
    private const float MeleeHitTime = 1.307f / 1.5f;
    private const float RangedHitTime = 1.001f;
    private const float BossMeleeHitTime = 1.296f;
    private const float BossSlamJumpTime = 0.584f;
    private const float BossSlamTravelTime = 1.551f - BossSlamJumpTime;

    // 动画状态：0 待机，1 移动，2 硬直，3 近战，4 射击，5 冲锋，6 砸地。
    private bool TryStartAttack(ServerEntityRecord enemy, Transform target, float distance, bool boss, bool ranged)
    {
        uint tick = server.ServerTick;
        if (tick < enemy.NextAttackTick || !HasAttackSight(enemy.Entity.transform.position, target.position)) return false;
        byte attack = 0;
        float windup = boss ? BossMeleeHitTime : ranged ? RangedHitTime : MeleeHitTime;
        float range = boss ? 3f : 2.3f;
        float cooldown = boss ? 1.5f : ranged ? 2f : 2.5f;
        if (boss && distance >= 4f && distance <= 15f && tick >= enemy.NextChargeTick)
        {
            attack = 5;
            windup = 0.8f;
            range = distance + 3f;
            enemy.NextChargeTick = tick + SecondsToTicks(6f);
        }
        else if (boss && distance <= 10f && tick >= enemy.NextSlamTick)
        {
            attack = 6;
            windup = BossSlamJumpTime;
            range = 2f;
            enemy.NextSlamTick = tick + SecondsToTicks(8f);
        }
        else if (distance <= (boss ? 2.5f : ranged ? 7f : 1.8f)) attack = (byte)(ranged ? 4 : 3);
        if (attack == 0) return false;
        enemy.Attack = attack;
        enemy.AnimationState = attack;
        enemy.AttackPhase = 0;
        enemy.AttackTime = windup;
        enemy.AttackDirection = enemy.Entity.transform.forward;
        enemy.AttackPosition = attack == 5 ? enemy.Entity.transform.position + enemy.AttackDirection * range : target.position;
        enemy.AttackHits.Clear();
        enemy.NextAttackTick = tick + SecondsToTicks(windup + cooldown);
        server.BroadcastBattleEvent(new BattleEventMessage
        {
            EventType = BattleEventType.EnemyAttackStarted, SourceEntityId = enemy.Entity.EntityId,
            TargetEntityId = enemy.TargetEntityId, SkillSlot = attack, Direction = enemy.AttackDirection,
            Position = attack == 6 ? enemy.AttackPosition : enemy.Entity.transform.position, Range = range, Duration = windup
        });
        return true;
    }

    private bool UpdateAttack(ServerEntityRecord enemy, float deltaTime)
    {
        if (enemy.Attack == 0) return false;
        enemy.AnimationState = enemy.Attack;
        enemy.AttackTime -= deltaTime;
        if (enemy.AttackPhase == 2)
        {
            if (enemy.AttackTime <= 0f) CancelAttack(enemy);
            return true;
        }
        if (enemy.AttackPhase == 0)
        {
            if (!playerManager.TryGetAlivePlayer(enemy.TargetEntityId, out Transform target)) { CancelAttack(enemy); return true; }
            if (enemy.AttackTime > 0f) return true;
            if (enemy.Attack == 3)
            {
                Vector3 offset = target.position - enemy.Entity.transform.position;
                offset.y = 0f;
                bool boss = enemy.Entity.EntityType == NetworkEntityType.Boss;
                float reach = boss ? 3f : 2.3f;
                if (offset.sqrMagnitude <= reach * reach && Vector3.Dot(enemy.AttackDirection, offset.normalized) >= 0.35f &&
                    HasAttackSight(enemy.Entity.transform.position, target.position))
                    playerManager.ApplyPlayerDamage(enemy.TargetEntityId, new DamageInfo(boss ? 30f : 15f, enemy.Entity.gameObject,
                        target.position, enemy.AttackDirection, Vector3.up), enemy.Entity.EntityId,
                        boss ? PlayerHitKind.Heavy : PlayerHitKind.Normal);
                Recover(enemy);
            }
            else if (enemy.Attack == 4)
            {
                Vector3 origin = enemy.Entity.transform.position + Vector3.up * 1.2f + enemy.AttackDirection * 0.65f;
                if (HasAttackSight(enemy.Entity.transform.position, target.position))
                    server.GetComponent<ServerProjectileRegistry>().SpawnEnemyProjectile(enemy.Entity.EntityId, enemy.TargetEntityId,
                        origin, target.position + Vector3.up - origin, 17f);
                Recover(enemy);
            }
            else
            {
                enemy.AttackPhase = 1;
                enemy.AttackTime = enemy.Attack == 5 ? Vector3.Distance(enemy.Entity.transform.position, enemy.AttackPosition) / 20f : BossSlamTravelTime;
                Vector3 travel = enemy.AttackPosition - enemy.Entity.transform.position;
                travel.y = 0f;
                enemy.Velocity = enemy.Attack == 5 ? enemy.AttackDirection * 20f : travel / BossSlamTravelTime;
            }
            return true;
        }
        if (enemy.AttackTime <= 0f)
        {
            if (enemy.Attack == 6)
                playerManager.DamagePlayersInArea(enemy.Entity.gameObject, enemy.Entity.transform.position, enemy.Entity.transform.position, 2f, 1f,
                    null, PlayerHitKind.Heavy);
            Recover(enemy);
            return true;
        }
        Vector3 remaining = enemy.AttackPosition - enemy.Entity.transform.position;
        remaining.y = 0f;
        enemy.Velocity = enemy.Attack == 5 ? enemy.AttackDirection * 20f : remaining / Mathf.Max(deltaTime, enemy.AttackTime);
        return true;
    }

    // 使用 Motor 实际移动路径判定冲锋接触，同一轮攻击对每位玩家只结算一次。
    private void ResolveChargeContact(ServerEntityRecord enemy, Vector3 previous)
    {
        if (enemy.Attack != 5 || enemy.AttackPhase != 1) return;
        playerManager.DamagePlayersInArea(enemy.Entity.gameObject, previous, enemy.Entity.transform.position, 1.5f, 40f, enemy.AttackHits,
            PlayerHitKind.Heavy);
        if ((enemy.Entity.transform.position - previous).sqrMagnitude < 0.0001f && enemy.AttackTime > 0f) Recover(enemy);
    }

    private static uint SecondsToTicks(float seconds) => (uint)Mathf.Max(1, Mathf.CeilToInt(seconds * NetworkRuntime.DefaultTickRate));

    private void Recover(ServerEntityRecord enemy)
    {
        enemy.AttackPhase = 2;
        enemy.AttackTime = 0.5f;
        enemy.Velocity = Vector3.zero;
        BroadcastAttackStopped(enemy);
    }

    private void CancelAttack(ServerEntityRecord enemy)
    {
        if (enemy.Attack != 0 && enemy.AttackPhase != 2) BroadcastAttackStopped(enemy);
        enemy.Attack = 0;
        enemy.AttackPhase = 0;
        enemy.AnimationState = 0;
    }

    private void BroadcastAttackStopped(ServerEntityRecord enemy)
    {
        server.BroadcastBattleEvent(new BattleEventMessage { EventType = BattleEventType.EnemyAttackStopped,
            SourceEntityId = enemy.Entity.EntityId, SkillSlot = enemy.Attack, Position = enemy.Entity.transform.position });
    }

    public static bool HasAttackSight(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < 0.0001f) return true;
        foreach (RaycastHit hit in Physics.RaycastAll(start + Vector3.up, delta.normalized, delta.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (!NetworkCharacterWorld.IsCharacterCollider(hit.collider)) return false;
        return true;
    }
}
