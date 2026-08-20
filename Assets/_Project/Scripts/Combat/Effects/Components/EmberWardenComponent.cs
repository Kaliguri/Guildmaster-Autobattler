using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>«Тёплый пепел»</b> — пассивка Хранителя очага (карточка [[the-hearth]]): угли НА СОЮЗНИКАХ
    /// перестают быть только уязвимостью и начинают их закалять. За каждый стак «Углей» союзник
    /// получает сопротивление дебаффам и небольшой реген; действует на всех живых своих на поле.
    /// <para><b>Числа:</b> <c>_emberEffect</c> — какой эффект считать (тот же «Уголь», что горит на
    /// врагах); <c>_tenacityPerStack</c> — сокращение длительности входящих дебаффов за стак
    /// (0.01 = −1%); <c>_maxTenacity</c> — потолок сокращения (0.5 = −50%); <c>_healPerStack</c> —
    /// лечение в секунду за стак; <c>_maxHealStacks</c> — сколько стаков учитывается в лечении.</para>
    /// <para><b>Когда срабатывает:</b> раз в <see cref="Interval"/> секунду, пока носитель жив.
    /// Погиб Хранитель — эффект уходит, а вместе с ним и вся выданная стойкость.</para>
    /// </summary>
    /// <remarks>
    /// Почему аура у ХРАНИТЕЛЯ, а не бонус внутри самого «Угля»: уголь один и тот же для всех и всегда
    /// негативный — на союзнике он такая же уязвимость к огню, как на враге (решение 2026-08-03). Знание
    /// «угли на своих можно обратить в пользу» принадлежит тому, кто делится огнём, а не огню; иначе
    /// эффект пришлось бы учить различать стороны, и второй огненный кит молча раздавал бы стойкость.
    /// <para><b>Готча:</b> стойкость сокращает длительность и потому <b>не действует на сам «Уголь»</b> —
    /// он бессрочный (<c>baseDuration = -1</c>), а бессрочному длительность не масштабируется
    /// (<c>EffectSystem.ResolveDurationTicks</c>). Уголь сходит своим таймером, и ускоряет этот сход
    /// <see cref="EmberComponent"/>, а не стойкость.</para>
    /// <para>Модификатор переустанавливается целиком на каждом тике по ключу самого эффекта: число
    /// стаков у союзника меняется всё время, и держать «дельту» значило бы вести второй счёт рядом с
    /// тем, что уже знает <c>Stats</c>.</para>
    /// </remarks>
    [Serializable]
    public sealed class EmberWardenComponent : IPeriodicComponent
    {
        [Tooltip("Эффект «Угли» — тот же, что горит на врагах. Считаются его стаки на каждом союзнике.")]
        [SerializeField] private EffectData _emberEffect;

        [Tooltip("Сокращение длительности входящих дебаффов за стак (0.01 = −1%).")]
        [SerializeField] private float _tenacityPerStack = 0.01f;

        [Tooltip("Потолок сокращения длительности (0.5 = −50%).")]
        [SerializeField] private float _maxTenacity = 0.5f;

        [Tooltip("Лечение в секунду за каждый стак «Углей» на союзнике.")]
        [SerializeField] private float _healPerStack = 1f;

        [Tooltip("Сколько стаков учитывается в лечении — потолок его роста.")]
        [SerializeField] private int _maxHealStacks = 50;

        /// <summary>Раз в секунду: и лечение порцией за секунду, и пересчёт стойкости по свежим стакам.</summary>
        public float Interval => 1f;

        /// <summary>Буфер поиска союзников. Компонент stateless по бою, буфер только чтобы не мусорить.</summary>
        [NonSerialized] private readonly List<RuntimeUnit> _allies = new List<RuntimeUnit>();

        public void OnApply(in EffectContext ctx) { }

        /// <summary>
        /// Носитель выбыл — снимаем выданную стойкость со всех, кому успели её выдать. Без этого
        /// сокращение дебаффов пережило бы Хранителя: моды сняты с него, а стоят на чужих статах.
        /// </summary>
        public void OnExpire(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || ctx.Combat == null) return;

            _allies.Clear();
            ctx.Combat.QueryUnitsInRadius(self.Position, ctx.Combat.Tuning.GlobalSearchRadius,
                _allies, TargetFilter.Allies, self.Team);

            for (int i = 0; i < _allies.Count; i++)
                _allies[i].Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
        }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _emberEffect == null || ctx.Combat == null) return;

            _allies.Clear();
            ctx.Combat.QueryUnitsInRadius(self.Position, ctx.Combat.Tuning.GlobalSearchRadius,
                _allies, TargetFilter.Allies, self.Team);

            for (int i = 0; i < _allies.Count; i++)
            {
                RuntimeUnit ally = _allies[i];
                if (ally == null || ally.IsDead) continue;

                int stacks = EmberStacks(ally);
                if (stacks <= 0)
                {
                    // Угли сошли — стойкость уходит вместе с ними, иначе она держалась бы вечно.
                    ally.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);
                    continue;
                }

                ApplyTenacity(ally, stacks, ctx);
                Warm(ally, stacks, self, ctx);
            }
        }

        /// <summary>Сокращение длительности входящих дебаффов: −1% за стак до потолка.</summary>
        private void ApplyTenacity(RuntimeUnit ally, int stacks, in EffectContext ctx)
        {
            if (_tenacityPerStack <= 0f || ally.Stats == null) return;

            float reduction = Mathf.Min(stacks * _tenacityPerStack, _maxTenacity);

            // ReceiveDebuffEff — множитель вокруг единицы: сокращение идёт минусом к нему.
            ally.Stats.AddModifiersFrom(ctx.Effect, new[]
            {
                new StatModifier(StatType.ReceiveDebuffEff, ModifierOp.PercentAdd, -reduction),
            }, deferred: true);
        }

        /// <summary>Тепло углей: лечение за секунду, растущее со стаками до своего потолка.</summary>
        private void Warm(RuntimeUnit ally, int stacks, RuntimeUnit self, in EffectContext ctx)
        {
            if (_healPerStack <= 0f) return;

            int counted = _maxHealStacks > 0 ? Mathf.Min(stacks, _maxHealStacks) : stacks;
            ctx.Combat.Heal(ally, counted * _healPerStack, self);
        }

        /// <summary>
        /// Стаки «Углей» на юните; 0 — если их нет. Читается <c>VisibleStacks</c> — снимок на начало
        /// тика, как везде: уголёк, легший этим же тиком, в этот же тик греть не должен.
        /// </summary>
        private int EmberStacks(RuntimeUnit unit)
        {
            List<RuntimeEffect> effects = unit.ActiveEffects;
            for (int i = 0; i < effects.Count; i++)
                if (ReferenceEquals(effects[i].Def, _emberEffect)) return effects[i].VisibleStacks;

            return 0;
        }
    }
}
