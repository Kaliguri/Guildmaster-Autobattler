using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Изворотливость» (§9.3, §9.4, §10.5): pre-damage реактив с зарядами. Полностью отменяет входящую
    /// АВТОАТАКУ (<see cref="DamageRequest.IsAutoAttack"/>) — любую, даже слабую; урон способностей/DoT/шипов
    /// не гасит. Дополнительно фильтруется триггером блока F (из <c>self.Unit.Ai.PassiveTrigger</c>) и тратит
    /// один заряд; заряды восстанавливаются независимо. Состояние зарядов — per-effect в
    /// <see cref="RuntimeEffect.TryConsumeCharge"/>.
    /// <para><b>Трата заряда — это КУВЫРОК</b> (решение 2026-07-26): носитель уходит с места на
    /// <see cref="_rollDistance"/> и получает <see cref="_hasteBuff"/> — ускорение после переката.
    /// Уклонение не просто гасит урон, а даёт занять позицию: оторваться, дойти, зайти в спину.</para>
    /// <para><b>Направление = направление собственного намерения.</b> Двигался этим тиком — кувырок
    /// по ходу движения (к цели, в обход, отступая); стоял и бил — кувырок ОТ атакующего. Уклонение
    /// никогда не сбивает план юнита, а ускоряет его, поэтому отдельного «отскока назад» нет.</para>
    /// <para><b>Числа:</b> <c>_maxCharges</c> — сколько автоатак подряд можно отменить;
    /// <c>_rechargeSeconds</c> — за сколько восстанавливается ОДИН заряд; <c>_rollDistance</c> — на
    /// сколько единиц уносит перекат; <c>_rollSpeedPerSecond</c> — как быстро (вместе с дистанцией
    /// задаёт длительность переката); <c>_hasteBuff</c> — баф ускорения, его величины живут в нём.
    /// Величины урона здесь нет намеренно: уклонение не смягчает удар, а отменяет его целиком.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, до расчёта урона — отменённый удар не наносит
    /// ничего и не будит реактивы «на удар» (шипы об уклонившегося не колются).</para>
    /// </summary>
    [Serializable]
    /// <remarks>
    /// Требует дееспособности (<see cref="IRequiresAgencyComponent"/>): «Изворотливость» — это кувырок с
    /// уходом с места, то есть ДЕЙСТВИЕ. Оглушённый ассасин уклоняться не может (решение Макса 2026-07-29).
    /// </remarks>
    public sealed class DodgeComponent : IPreDamageComponent, IStackableComponent, IRequiresAgencyComponent
    {
        [Tooltip("Число зарядов негейта. Убийца = 1 (гейтит одну следующую атаку).")]
        [SerializeField] private int _maxCharges = 1;

        [Tooltip("Независимая перезарядка одного заряда, сек. Убийца = 5.")]
        [SerializeField] private float _rechargeSeconds = 5f;

        [Tooltip("Дистанция кувырка, мировых единиц. 0 = уклонение без ухода с места.")]
        [SerializeField] private float _rollDistance = 2f;

        [Tooltip("Скорость кувырка, ед/сек: с дистанцией задаёт его длительность. 0 = общий дефолт смещения.")]
        [SerializeField] private float _rollSpeedPerSecond = 12f;

        [Tooltip("Баф ускорения после кувырка (скорость передвижения и его длительность живут в нём).")]
        [SerializeField] private EffectData _hasteBuff;

        [Tooltip("Конвертации статов в перезарядку заряда (M4). Убийца: обратная форма от AttackSpeed — " +
                 "быстрый Убийца уклоняется чаще, но кулдаун никогда не станет нулём.")]
        [SerializeField] private Data.Stats.StatConversion[] _rechargeScalings;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.ArmCharges(_maxCharges);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ трогает заряды: их число фиксировано (_maxCharges), а per-charge таймеры
            // перезарядки уже живут в самом эффекте. Дефолтный OnExpire→OnApply здесь
            // обнулил бы массив — бесплатный рефилл всех зарядов негейта на каждый стак (07 §3.8 B2).
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return; // уже отменён другим компонентом
            if (!incoming.IsAutoAttack) return; // «Изворотливость» уклоняется ТОЛЬКО от автоатак (не от способностей/DoT)

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;
            if (!TriggerMet(self, in incoming)) return;

            // Перезарядка — через конвертации (M4): у быстрого носителя заряд возвращается чаще.
            float rechargeSeconds = Data.Stats.StatConversion.ApplyAll(_rechargeScalings, _rechargeSeconds, self.Stats);
            int rechargeTicks = Mathf.Max(1, Mathf.RoundToInt(rechargeSeconds * SimConstants.TickRate));

            // Нет готовых зарядов — удар проходит.
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, rechargeTicks)) return;

            result.Negated = true;
            Roll(self, in incoming, in ctx);
        }

        /// <summary>
        /// Кувырок: уход с места по направлению собственного намерения + баф ускорения. Смещение идёт
        /// тем же швом, что рывок Монаха (носитель = и цель, и источник), поэтому о стену не
        /// наказывается и не приносит урона.
        /// </summary>
        private void Roll(RuntimeUnit self, in DamageRequest incoming, in EffectContext ctx)
        {
            if (_hasteBuff != null) ctx.Combat.ApplyEffect(self, _hasteBuff, self);
            if (_rollDistance <= 0f) return;

            ctx.Combat.Displace(new DisplaceRequest(
                self, self, RollDirection(self, in incoming), _rollDistance,
                cannonball: false, damage: 0f, school: self.DamageSchool, width: 0f,
                speedPerSecond: _rollSpeedPerSecond));
        }

        /// <summary>
        /// Куда катиться. Движение этого тика — уже готовый вектор намерения: MovementSystem отработал
        /// до автоатак, значит смещение позиции показывает, куда юнит шёл (к цели, в обход, отступая).
        /// Стоял на месте (бьёт вплотную) — катимся ОТ атакующего. Совсем без ориентира (атакующего
        /// нет — DoT, ловушка) — катимся вперёд по последнему известному курсу, иначе вправо: чистый
        /// фолбэк на невозможный в бою случай, лишь бы не делить на ноль.
        /// </summary>
        private static Vector2 RollDirection(RuntimeUnit self, in DamageRequest incoming)
        {
            Vector2 intent = self.Position - self.PreviousPosition;
            if (intent.sqrMagnitude > 1e-6f) return intent;

            RuntimeUnit attacker = incoming.Source;
            if (attacker != null && !ReferenceEquals(attacker, self))
            {
                Vector2 away = self.Position - attacker.Position;
                if (away.sqrMagnitude > 1e-6f) return away;
            }

            return Vector2.right;
        }

        /// <summary>Триггер блока F: None — никогда; AnyHit/Always — любой удар; OnHitAbovePctMaxHp — выше порога ИЛИ смертельный.</summary>
        private static bool TriggerMet(RuntimeUnit self, in DamageRequest req)
        {
            AIProfile ai = self.Unit != null ? self.Unit.Ai : null;
            PassiveTrigger trigger = ai != null ? ai.PassiveTrigger : PassiveTrigger.AnyHit;

            switch (trigger)
            {
                case PassiveTrigger.None:
                    return false;
                case PassiveTrigger.AnyHit:
                case PassiveTrigger.Always:
                    return true;
                case PassiveTrigger.OnHitAbovePctMaxHp:
                    float threshold = (ai != null ? ai.PassiveThresholdPct : AIProfile.DefaultPassiveThresholdPct) * self.Stats.Get(StatType.MaxHP);
                    return req.RawDamage > threshold || req.RawDamage >= self.CurrentHP;
                default:
                    return false;
            }
        }
    }
}
