using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Пылающие клинки» (Огненный мечник): каждый удар клинком разгоняет скорость атаки носителя
    /// (стакающийся баф <see cref="_rampEffect"/>) и стоит ему части СВОЕГО здоровья — плата за разгон.
    /// Поджог цели вешается штатным on-hit эффектом авто-атаки (<c>UnitData._autoAttackEffects</c>),
    /// здесь только само-эффекты.
    /// <para><b>Числа:</b> <c>_selfDamagePctCurrentHp</c> — плата за удар, доля ТЕКУЩЕГО здоровья
    /// (0.01 = 1%); урон Чистый, поэтому своя броня его не гасит, и до нуля он не добивает.
    /// <c>_rampEffect</c> — стакающийся баф скорости атаки, который и есть разгон (его величина и
    /// потолок живут в самом эффекте, не здесь).</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.DamageDealt"/> носителя.</para>
    /// </summary>
    /// <remarks>
    /// Реагирует только на АВТОАТАКИ и только по чужой цели: собственный урон по себе автоатакой не
    /// является, поэтому само-подкачки/рекурсии нет.
    /// </remarks>
    [Serializable]
    public sealed class BlazingBladesComponent : IReactiveComponent
    {
        [Tooltip("Само-урон за удар, доля ТЕКУЩЕГО HP (0.01 = 1%). Идёт мимо брони (True).")]
        [SerializeField] private float _selfDamagePctCurrentHp = 0.01f;

        [Tooltip("Стакающийся баф разгона (скорость атаки). Потолок задаётся его MaxStacks.")]
        [SerializeField] private EffectData _rampEffect;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (!e.IsAutoAttack) return;

            // Удар по горящей цели расщепляется на клинок и огонь — это ДВА урон-события на один
            // взмах. Разгон и самоурон вешаем только на клинковую половину, иначе темп копится вдвое
            // быстрее заявленного, а мечник платит здоровьем дважды за один удар.
            if (e.IsFire) return;

            RuntimeUnit self = ctx.Target; // DamageDealt доставляется НАНЁСШЕМУ урон
            if (self == null || self.IsDead) return;
            if (e.Target == self) return;  // само-урон не разгоняет

            if (_rampEffect != null)
                ctx.Combat.ApplyEffect(self, _rampEffect, self);

            float selfDamage = self.CurrentHP * _selfDamagePctCurrentHp;
            if (selfDamage <= 0f) return;

            // Плата за клинки: True — не гасится собственной бронёй; не автоатака — не рекурсирует сюда.
            ctx.Combat.DealDamage(new DamageRequest(self, self, selfDamage, DamageType.Pure, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Reactive));
        }
    }
}
