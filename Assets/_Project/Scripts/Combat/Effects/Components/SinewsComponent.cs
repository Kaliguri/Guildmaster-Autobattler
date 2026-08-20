using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Жилы» Десятины: пока носитель здоров, он рвёт себя ради темпа — бьёт быстрее, но каждый удар
    /// стоит ему доли собственного запаса. Работает только в заданной боевой стойке.
    /// <para><b>Числа:</b> <c>_requiredStance</c> — индекс стойки, в которой навык жив
    /// (<see cref="AttackStanceComponent.CloseStanceIndex"/> у Десятины; −1 = в любой);
    /// <c>_hpThresholdPct</c> — доля максимума, ВЫШЕ которой навык работает (0.3 = «пока больше 30%»);
    /// <c>_attackSpeedBonusPct</c> — прибавка к скорости атаки, доли (0.5 = +50%);
    /// <c>_costPctMaxHp</c> — плата за удар в долях МАКСИМАЛЬНОГО HP (0.04 = 4%).</para>
    /// <para><b>Когда срабатывает:</b> периодически — пересматривает, держать ли бонус; и на каждой
    /// авто-атаке носителя — берёт плату.</para>
    /// </summary>
    /// <remarks>
    /// <b>ПорогHP гейтит и бонус, и плату разом</b> — поэтому плата не может убить: ниже порога навык
    /// уже выключен, а порог заведомо выше нуля. Разведи их — и кит начал бы добивать себя ударами по
    /// пустому запасу, чего карточка не просит.
    /// <para><b>Плата берётся прямым вычетом HP, а не уроном</b> (тот же путь, что у «Голода»,
    /// см. <see cref="TitheComponent"/>): это не удар, поэтому ни броня, ни щиты, ни реакции «по
    /// мне попали», ни вампиризм её не видят. Через <c>DealDamage</c> кит лечил бы себя собственной
    /// платой, если бы взял вампиризм, и цена навыка обнулилась бы.</para>
    /// <para><b>Живёт на СВОЁМ эффекте, а не внутри стойки.</b> Статовые модификаторы адресуются
    /// эффектом-ключом, и <see cref="AttackStanceComponent"/> уже держит на своём ключе статы формы —
    /// поселись «Жилы» там же, снятие бонуса сносило бы заодно дальность и темп формы.</para>
    /// <para><b>Состояние — в <c>RuntimeEffect.Counter</c>, а не в поле компонента:</b> компонент
    /// шарится между носителями, и поле «бонус висит» стало бы общим на всех.</para>
    /// </remarks>
    [Serializable]
    public sealed class SinewsComponent : IPeriodicComponent, IReactiveComponent
    {
        [Tooltip("Индекс боевой стойки, в которой навык работает. −1 = в любой стойке.")]
        [SerializeField] private int _requiredStance = AttackStanceComponent.CloseStanceIndex;

        [Tooltip("Доля максимального HP, ВЫШЕ которой навык жив (0.3 = «пока больше 30% HP»).")]
        [Range(0f, 1f)]
        [SerializeField] private float _hpThresholdPct = 0.3f;

        [Tooltip("Прибавка к скорости атаки в долях (0.5 = +50%).")]
        [SerializeField] private float _attackSpeedBonusPct = 0.5f;

        [Tooltip("Плата за КАЖДУЮ авто-атаку в долях максимального HP (0.04 = 4%).")]
        [Range(0f, 1f)]
        [SerializeField] private float _costPctMaxHp = 0.04f;

        [Tooltip("Как часто пересматривать, жив ли навык, сек.")]
        [SerializeField] private float _checkInterval = 0.1f;

        public float Interval => _checkInterval > 0f ? _checkInterval : 0.1f;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
        }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || self.Stats == null) return;

            bool wanted = IsActive(self);
            bool applied = ctx.Effect.Counter != 0;
            if (wanted == applied) return;

            if (wanted)
            {
                // Отложенно — по закону видимости: баф, наложенный посреди тика, не должен менять
                // решения, уже принятые в этом тике.
                self.Stats.AddModifiersFrom(ctx.Effect, new[]
                {
                    new StatModifier(StatType.AttackSpeed, ModifierOp.PercentAdd, _attackSpeedBonusPct),
                }, deferred: true);
                ctx.Effect.Counter = 1;
            }
            else
            {
                self.Stats.RemoveModifiersFrom(ctx.Effect, deferred: true);
                ctx.Effect.Counter = 0;
            }
        }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Плата — только за собственный удар рукой. Тик DoT и ответка реактива ударами не считаются:
            // иначе кровь, которую кит же и пустил, доила бы его второй раз.
            if (!e.IsAutoAttack) return;

            RuntimeUnit self = ctx.Target;   // DamageDealt доставляется НАНЁСШЕМУ урон
            if (self == null || self.IsDead || self.Stats == null) return;
            if (!IsActive(self)) return;

            float cost = self.Stats.Get(StatType.MaxHP) * _costPctMaxHp;
            if (cost <= 0f) return;

            self.CurrentHP -= cost;
        }

        /// <summary>
        /// Жив ли навык сейчас: нужная стойка и запас ВЫШЕ порога. Одно правило на бонус и на плату —
        /// два условия разъехались бы на первом же изменении порога.
        /// </summary>
        private bool IsActive(RuntimeUnit self)
        {
            if (_requiredStance >= 0 && self.AttackStance != _requiredStance) return false;

            float maxHp = self.Stats.Get(StatType.MaxHP);
            return maxHp > 0f && self.CurrentHP > maxHp * _hpThresholdPct;
        }
    }
}
