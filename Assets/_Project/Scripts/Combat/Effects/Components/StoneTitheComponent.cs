using System;
using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Каменная десятина» Геоманта (карточка [[the-cairn]]): один раз в начале боя носитель забирает у
    /// союзников вокруг долю их ТЕКУЩЕГО HP и прибавляет себе половину забранного к максимуму.
    /// <para><b>Числа:</b> <c>_tithePctCurrentHp</c> — сколько берётся с каждого союзника (0.1 = 10% его
    /// текущего HP); <c>_keepShare</c> — какая доля забранного достаётся носителю (0.5 = половина);
    /// <c>_radius</c> — кого достаёт десятина. Сделка невыгодна арифметически и выгодна только
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
    /// <para><b>Прибавка к максимуму идёт вместе с лечением на ту же величину:</b> иначе носитель получил
    /// бы пустой запас и остался бы на своём 1% HP — то есть сделка ничего бы ему не дала.</para>
    /// </remarks>
    [Serializable]
    public sealed class StoneTitheComponent : IPeriodicComponent
    {
        [Tooltip("На какой доле максимума носитель начинает бой (0.01 = 1%). 0 = начинает как обычно, полным. " +
                 "Каменный аскет стартует почти мёртвым и добирает поеданием союзников — его живучесть " +
                 "определяется составом команды, а не карточкой.")]
        [Range(0f, 1f)]
        [SerializeField] private float _startAtHpPct;

        [Tooltip("Доля ТЕКУЩЕГО HP, забираемая у каждого союзника (0.1 = 10%).")]
        [Range(0f, 1f)]
        [SerializeField] private float _tithePctCurrentHp = 0.1f;

        [Tooltip("Какая доля забранного достаётся носителю (0.5 = половина). Остальное просто теряется.")]
        [SerializeField] private float _keepShare = 0.5f;

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

            float gained = taken * _keepShare;
            if (gained <= 0f) return;

            self.Stats.AddModifiersFrom(eff, new[]
            {
                new StatModifier(StatType.MaxHP, ModifierOp.Flat, gained),
            }, deferred: false);

            // Запас без наполнения бесполезен: носитель стартует почти мёртвым, и вся сделка ради того,
            // чтобы он встал на ноги. Поэтому прибавка к максимуму сразу приходит и в текущее HP.
            self.CurrentHP += gained;
        }
    }
}
