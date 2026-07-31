using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Швырнуть союзника</b> (гоблин-командир «Швырнуть!», наездник «Доставка!»): носитель хватает
    /// соседнего союзника ближнего боя и запускает его во врага. Летящий союзник работает живым снарядом —
    /// урон и оглушение достаются обоим, и ему, и тому, в кого попали.
    /// <para><b>Числа:</b> <c>_allyRadius</c> — где искать «снаряд»; <c>_enemyRadius</c> — насколько близко
    /// должен быть враг, чтобы бросок имел смысл; <c>_flightDistance</c> и <c>_width</c> — дальность и
    /// коридор полёта; <c>_damageMultiplier</c> — урон от силы атаки носителя, ОДИН И ТОТ ЖЕ обоим
    /// (вердикт Макса 2026-07-31); <c>_stun</c> — оглушение обоим; <c>_cooldownSeconds</c> — перезарядка
    /// броска.</para>
    /// <para><b>Когда срабатывает:</b> тиком, когда рядом есть и подходящий союзник, и враг, — и заряд
    /// броска готов.</para>
    /// </summary>
    /// <remarks>
    /// <b>Урон поровну — решение Макса.</b> Гоблины платят своими, и это читается как их характер: бросок
    /// одинаково больно и цели, и снаряду. Ослабить долю снаряда значило бы сделать приём чисто выгодным.
    /// <para><b>Летящий получает оглушение дважды по разным причинам</b> — полётное (его даёт сам
    /// <c>DisplacementSystem</c>, как любому смещаемому) и эффект <see cref="_stun"/>, который переживает
    /// приземление. Это не дубль: первое кончается вместе с полётом, второе — отдельная цена броска.</para>
    /// <para><b>«Не элиту» карточки выразить нечем:</b> флага элитности у юнита нет, поэтому берётся любой
    /// союзник ближнего боя, кроме самого носителя. Расхождение помечено в статусе врагов.</para>
    /// </remarks>
    [Serializable]
    public sealed class ThrowAllyComponent : IPeriodicComponent
    {
        private static readonly List<RuntimeUnit> Nearby = new List<RuntimeUnit>(16);

        [Tooltip("Враг должен быть в этом радиусе — иначе бросать некуда.")]
        [Min(0.5f)]
        [SerializeField] private float _enemyRadius = 7f;

        [Tooltip("Дальность полёта союзника, мировые единицы.")]
        [Min(0.5f)]
        [SerializeField] private float _flightDistance = 5f;

        [Tooltip("Ширина коридора полёта: кого ещё задевает живой снаряд.")]
        [Min(0.1f)]
        [SerializeField] private float _width = 1f;

        [Tooltip("Урон = множитель × сила атаки носителя. Одно и то же число достаётся цели и снаряду.")]
        [Min(0f)]
        [SerializeField] private float _damageMultiplier = 1.5f;

        [Tooltip("Тип урона броска.")]
        [SerializeField] private DamageType _damageType = DamageType.Blunt;

        [Tooltip("Оглушение, которое получают оба — цель и брошенный союзник.")]
        [SerializeField] private EffectData _stun;

        [Tooltip("Перезарядка броска, сек.")]
        [Min(0.5f)]
        [SerializeField] private float _cooldownSeconds = 12f;

        public float Interval => 1f / Core.Simulation.SimConstants.TickRate;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.ArmCharges(1);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || ctx.Combat == null) return;

            RuntimeUnit victim = NearestEnemy(self, ctx, _enemyRadius);
            if (victim == null) return;

            RuntimeUnit projectile = NearestMeleeAlly(self, ctx);
            if (projectile == null) return;

            int cooldown = Mathf.Max(1, Mathf.RoundToInt(_cooldownSeconds * Core.Simulation.SimConstants.TickRate));
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, cooldown)) return;

            Vector2 toVictim = victim.Position - projectile.Position;
            Vector2 dir = toVictim.sqrMagnitude > 1e-6f ? toVictim.normalized : Vector2.right;
            float damage = self.Stats.Get(StatType.AutoAttackDamage) * _damageMultiplier;

            // Живой снаряд летит «ядром»: урон на линии наносит DisplacementSystem, и он же выдаёт
            // полётное оглушение. Источник урона — носитель: бросок его, а не брошенного.
            ctx.Combat.Displace(new DisplaceRequest(
                projectile, self, dir, _flightDistance,
                cannonball: true, damage: damage, damageType: _damageType, width: _width));

            // Снаряду — та же цифра, что цели (вердикт Макса), и оглушение обоим.
            ctx.Combat.DealDamage(new DamageRequest(self, projectile, damage, _damageType, ctx.Combat.ArmorK));
            if (_stun == null) return;

            ctx.Combat.ApplyEffect(projectile, _stun, self);
            ctx.Combat.ApplyEffect(victim, _stun, self);
        }

        /// <summary>Ближайший живой враг в радиусе; тай-брейк по Id — детерминизм.</summary>
        private static RuntimeUnit NearestEnemy(RuntimeUnit self, in EffectContext ctx, float radius)
        {
            Nearby.Clear();
            ctx.Combat.QueryUnitsInRadius(self.Position, radius, Nearby, TargetFilter.Enemies, self.Team);
            return Closest(self, meleeOnly: false);
        }

        /// <summary>
        /// Ближайший живой союзник ближнего боя, кроме самого носителя: дальнобойного не швыряют — он и
        /// стоит не там, и нужен на своём месте.
        /// </summary>
        private static RuntimeUnit NearestMeleeAlly(RuntimeUnit self, in EffectContext ctx)
        {
            Nearby.Clear();
            ctx.Combat.QueryUnitsInRadius(self.Position, self.Stats.Get(StatType.Size) + 2.5f,
                Nearby, TargetFilter.Allies, self.Team);
            Nearby.Remove(self);
            return Closest(self, meleeOnly: true);
        }

        /// <summary>
        /// Ближайший из <see cref="Nearby"/>. Буфер очищается здесь же — он общий на все носители, и
        /// оставленный хвост достался бы следующему.
        /// </summary>
        private static RuntimeUnit Closest(RuntimeUnit self, bool meleeOnly)
        {
            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Nearby.Count; i++)
            {
                RuntimeUnit u = Nearby[i];
                if (u == null || u.IsDead || u == self) continue;
                if (meleeOnly && u.AttackType != AttackType.Melee) continue;

                float sq = (u.Position - self.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && best != null && u.Id < best.Id))
                {
                    bestSq = sq;
                    best = u;
                }
            }
            Nearby.Clear();
            return best;
        }
    }
}
