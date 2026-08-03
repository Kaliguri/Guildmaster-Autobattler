using System;
using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Десятина» Кровоманта (карточка [[the-tithe]]): один раз в начале боя носитель забирает у
    /// союзников вокруг долю их ТЕКУЩЕГО HP и прибавляет себе половину забранного к максимуму.
    /// <para><b>Числа:</b> <c>_tithePctCurrentHp</c> — сколько берётся с каждого союзника (0.2 = 20% его
    /// текущего HP); <c>_keepShare</c> — доля забранного, уходящая носителю в МАКСИМУМ (0.3);
    /// <c>_healShareOfTaken</c> — доля забранного, уходящая в ТЕКУЩЕЕ HP (1 = всё);
    /// <c>_radius</c> — кого достаёт десятина. Сделка невыгодна арифметически по запасу и выгодна
    /// позиционно: команда переливает живучесть из тех, кого не фокусят, в того, кто дерётся.</para>
    /// <para><b>Когда срабатывает:</b> ровно один раз, на первом же тике боя.</para>
    /// </summary>
    /// <remarks>
    /// <b>Платят ТЕКУЩИМ HP, а не максимумом</b> (вердикт Макса 2026-07-30): союзники начинают бой на 90%,
    /// их запас не меняется. Так цена отыгрываемая — хилерам есть что лечить, — и классовые числа остаются
    /// целыми. Вариант «снять максимум» делал бы танка не танком, а «снять на весь забег» — это уже
    /// Последствие, и оно конфликтует с каноном травм.
    /// <para><b>Почему периодический компонент, а не <c>OnApply</c>.</b> Пассивки накладываются в момент
    /// сборки юнита, когда остальной отряд ещё не стоит на арене: десятина собрала бы дань с половины
    /// команды или ни с кого. Первый тик — самая ранняя точка, где мир уже собран целиком.</para>
    /// <para><b>Запас и наполнение — два РАЗНЫХ числа</b> (вердикт Макса 2026-07-30). Лечение считается от
    /// всего забранного (100%), а к максимуму уходит только <c>_keepShare</c> (30%). Одно число здесь не
    /// работает: если лечить на долю запаса, носитель остаётся почти мёртвым и сделка ему ничего не даёт;
    /// если поднимать запас на всё забранное, он превращается в танка чужими телами. Разведение даёт то,
    /// что нужно: он встаёт на ноги сразу, но его потолок растёт втрое медленнее.</para>
    /// </remarks>
    [Serializable]
    // Имя без «Каменной»: кит — Кровомант, а не Геомант (03.08.2026). Прежнее имя класса держится
    // атрибутом, иначе SerializeReference в ассете эффекта не найдёт тип и компонент станет null.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceClassName: "StoneTitheComponent")]
    public sealed class TitheComponent : IPeriodicComponent
    {
        [Tooltip("На какой доле максимума носитель начинает бой (0.01 = 1%). 0 = начинает как обычно, полным. " +
                 "Кровомант стартует почти мёртвым и добирает поеданием союзников — его живучесть " +
                 "определяется составом команды, а не карточкой.")]
        [Range(0f, 1f)]
        [SerializeField] private float _startAtHpPct;

        [Tooltip("Доля ТЕКУЩЕГО HP, забираемая у каждого союзника (0.1 = 10%).")]
        [Range(0f, 1f)]
        [SerializeField] private float _tithePctCurrentHp = 0.1f;

        [Tooltip("Какая доля забранного идёт носителю В МАКСИМУМ HP (0.3 = треть). Остальное теряется.")]
        [SerializeField] private float _keepShare = 0.5f;

        [Tooltip("Какая доля забранного идёт носителю в ТЕКУЩЕЕ HP (1 = лечится на всё забранное). " +
                 "Отдельно от KeepShare: запас и наполнение — разные величины, см. remarks.")]
        [SerializeField] private float _healShareOfTaken = 1f;

        [Tooltip("Радиус сбора десятины, мировые единицы. 0 = без ограничения (вся команда).")]
        [SerializeField] private float _radius = 12f;

        public float Interval => 1f / SimConstants.TickRate;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            if (eff.Counter != 0) return;   // десятина берётся один раз за бой
            eff.Counter = 1;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            // Стартовая просадка идёт ДО сбора дани: иначе носитель сперва наполнился бы чужим HP, а
            // потом просадка срезала бы и его — сделка обнулилась бы сама.
            if (_startAtHpPct > 0f)
            {
                float start = self.Stats.Get(StatType.MaxHP) * _startAtHpPct;
                if (start < self.CurrentHP) self.CurrentHP = start;
            }

            var allies = new List<RuntimeUnit>();
            float radius = _radius > 0f ? _radius : ctx.Combat.Tuning.GlobalSearchRadius;
            ctx.Combat.QueryUnitsInRadius(self.Position, radius, allies, TargetFilter.Allies, self.Team);

            float taken = 0f;
            for (int i = 0; i < allies.Count; i++)
            {
                RuntimeUnit ally = allies[i];
                if (ally == null || ally.IsDead || ReferenceEquals(ally, self)) continue;

                float toll = ally.CurrentHP * _tithePctCurrentHp;
                if (toll <= 0f) continue;

                // Не через DealDamage: это не удар — ни брони, ни щитов, ни реакций «по мне попали».
                // Дань платится телом, а не боем, и убить союзника она не может (доля от текущего HP).
                ally.CurrentHP -= toll;
                taken += toll;
            }

            if (taken <= 0f) return;

            float gained = taken * _keepShare;
            if (gained > 0f)
            {
                self.Stats.AddModifiersFrom(eff, new[]
                {
                    new StatModifier(StatType.MaxHP, ModifierOp.Flat, gained),
                }, deferred: false);
            }

            // Наполнение считается от ЗАБРАННОГО, а не от прибавки к максимуму: носитель стартует почти
            // мёртвым, и сделка ради того, чтобы он встал на ноги. Клампим по максимуму — лишнее просто
            // теряется, как и доля, не ушедшая в запас.
            float healed = taken * _healShareOfTaken;
            if (healed > 0f)
            {
                float maxHp = self.Stats.Get(StatType.MaxHP);
                self.CurrentHP = self.CurrentHP + healed > maxHp ? maxHp : self.CurrentHP + healed;
            }
        }
    }
}
