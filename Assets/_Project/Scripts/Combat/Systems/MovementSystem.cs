using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Интегрирует позиции живых юнитов. Ручная математика — без Rigidbody2D и без Time.deltaTime
    /// (dt передаётся снаружи из <see cref="CombatLoopService"/>). Ветвится по
    /// <see cref="RuntimeUnit.Positioning"/> (§9.7): Approach (Ф1), Kite (полоса дистанции),
    /// Retreat (от ближайшего врага). Стрельба на ходу (§9.8) снимает рут замаха.
    /// </summary>
    public sealed class MovementSystem
    {
        // Порог «желаемый отход зажат стеной»: если кламп сдвинул цель дальше этого (в кв. ед.) — считаем,
        // что упёрлись, и переходим к репозиции вдоль стены. Мал, чтобы ловить реальный контакт, не FP-шум.
        private const float WallPinEpsilonSq = 1e-6f;

        /// <summary>Продвинуть позиции всех живых юнитов на один тик.</summary>
        /// <param name="units">Список всех юнитов в бою.</param>
        /// <param name="dt">Длительность тика (всегда <see cref="SimConstants.TickDelta"/>).</param>
        /// <param name="bounds">Границы поля: итоговая позиция клампится внутрь (<see cref="ArenaBounds.Unbounded"/> = без стен).</param>
        public void Tick(List<RuntimeUnit> units, float dt, in ArenaBounds bounds, in SimTuning tuning)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead) continue;

                // В полёте (§9.9) юнита двигает DisplacementSystem — сам он не перемещается.
                if (unit.DisplacedTicksRemaining > 0) continue;

                unit.PreviousPosition = unit.Position;

                // Контроль (корень/обездвиживание) — стоим на месте (вики «6» §5.3).
                if (!unit.CanMove) continue;

                bool windingUp        = unit.IsWindingUp;
                bool attackWhileMoving = unit.Relic != null && unit.Relic.CanAttackWhileMoving;

                // Замах авто-атаки рутит юнита (свинг на месте, вики «14») — КРОМЕ реликвий со
                // «стрельбой на ходу» (§9.8): те продолжают движение со штрафом скорости.
                if (windingUp && !attackWhileMoving) continue;

                RuntimeUnit target = unit.CurrentTarget;
                if (target == null) continue;

                float moveSpeed = unit.Stats.Get(StatType.MoveSpeed);
                if (windingUp && attackWhileMoving)
                    moveSpeed *= Mathf.Max(0f, 1f - unit.Relic.MovingAttackSpeedPenaltyPct); // §9.8

                float maxMove = moveSpeed * dt;
                if (maxMove <= 0f) continue;

                switch (unit.Positioning)
                {
                    case PositioningIntent.Kite:    MoveKite(unit, target, maxMove, bounds, in tuning); break;
                    case PositioningIntent.Retreat: MoveRetreat(unit, units, maxMove, bounds); break;
                    default:                        MoveApproach(unit, target, maxMove); break;
                }

                // Стены арены: не даём кайту/отступлению уйти за поле (вики «15» §7).
                unit.Position = bounds.Clamp(unit.Position);
            }
        }

        /// <summary>Сближение до дистанции атаки (поведение Ф1).</summary>
        private static void MoveApproach(RuntimeUnit unit, RuntimeUnit target, float maxMove)
        {
            float attackRange = unit.Stats.Get(StatType.AttackRange);
            Vector2 toTarget  = target.Position - unit.Position;
            float distSq      = toTarget.sqrMagnitude;
            float rangeSq     = attackRange * attackRange;

            if (distSq <= rangeSq) return;

            float dist = Mathf.Sqrt(distSq);
            if (dist - attackRange <= maxMove)
                unit.Position = target.Position - toTarget / dist * attackRange;
            else
                unit.Position += toTarget / dist * maxMove;
        }

        /// <summary>
        /// Кайт (§9.7): держим дистанцию в полосе [FleeDist, FallbackDist] из <see cref="Kite"/> профиля —
        /// отходим (до FallbackDist), если ближе FleeDist; подходим, если дальше FallbackDist; иначе стоим
        /// и стреляем. Провал/пустой контент → деградируем на [AttackRange×0.6, AttackRange] (07 §3.8 B4).
        /// </summary>
        private static void MoveKite(RuntimeUnit unit, RuntimeUnit target, float maxMove, in ArenaBounds bounds, in SimTuning tuning)
        {
            Kite kite = unit.Relic != null ? unit.Relic.Ai.Kite : default;
            float flee     = kite.FleeDist;
            float fallback = kite.FallbackDist;

            // Некорректные/незаданные дистанции (FallbackDist ≤ FleeDist или неположительные) —
            // фолбэк на радиус атаки, чтобы кайт не залипал. Валидатор контента (07 §3.8 C2) поймает
            // такое на авторинге; здесь — безопасная деградация в рантайме.
            if (flee <= 0f || fallback <= flee)
            {
                float range = unit.Stats.Get(StatType.AttackRange);
                flee     = range * tuning.KiteFleeFactor;
                fallback = range;
            }

            Vector2 toTarget = target.Position - unit.Position;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f) return;

            Vector2 dir = toTarget / dist;
            if (dist < flee)
                // Ближе FleeDist — отходим до FallbackDist. Отход «от цели» может упереться в стену:
                // тогда репозиционируемся вдоль стены/сквозь скопление в открытое место, а не утыкаемся.
                unit.Position = RetreatStep(unit.Position, -dir, Mathf.Min(maxMove, fallback - dist), bounds);
            else if (dist > fallback)
                unit.Position += dir * Mathf.Min(maxMove, dist - fallback);  // дальше FallbackDist — подходим
            // иначе — в полосе [FleeDist, FallbackDist], стоим (атакуем на ходу)
        }

        /// <summary>Отступление (§9.7): движемся прочь от ближайшего врага (тай-брейк по Id — детерминизм).</summary>
        private static void MoveRetreat(RuntimeUnit unit, List<RuntimeUnit> units, float maxMove, in ArenaBounds bounds)
        {
            RuntimeUnit nearest = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit o = units[i];
                if (o.IsDead || o.Team == unit.Team) continue;
                float sq = (o.Position - unit.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (nearest == null || o.Id < nearest.Id)))
                {
                    bestSq = sq;
                    nearest = o;
                }
            }
            if (nearest == null) return;

            Vector2 away = unit.Position - nearest.Position;
            if (away.sqrMagnitude < 1e-4f) return;
            unit.Position = RetreatStep(unit.Position, away.normalized, maxMove, bounds);
        }

        /// <summary>
        /// Шаг отхода с обработкой «зажат у стены» (вики «15» §7). Обычно уходим строго <paramref name="awayDir"/>.
        /// Но если этот отход упирается в стену арены (итог клампится) — репозиционируемся: скользим ВДОЛЬ стены
        /// в сторону центра арены (для окружённого юнита это уводит его мимо/сквозь скопление врагов в открытое
        /// место, а не утыкает в угол). Детерминировано: только векторная математика, без RNG.
        /// </summary>
        private static Vector2 RetreatStep(Vector2 pos, Vector2 awayDir, float step, in ArenaBounds bounds)
        {
            if (step <= 0f) return pos;

            Vector2 desired = pos + awayDir * step;
            Vector2 clamped = bounds.Clamp(desired);

            // Отход прошёл свободно (стена не поджала) — идём как есть.
            if ((clamped - desired).sqrMagnitude < WallPinEpsilonSq) return desired;

            // Уперлись в стену: касательная к направлению отхода, в сторону центра арены (открытое место).
            Vector2 tangent = new Vector2(-awayDir.y, awayDir.x);
            if (Vector2.Dot(tangent, bounds.Center - pos) < 0f) tangent = -tangent;
            if (tangent.sqrMagnitude < 1e-10f) return clamped; // вырожденный случай — хотя бы не за стеной

            return bounds.Clamp(pos + tangent.normalized * step);
        }
    }
}
