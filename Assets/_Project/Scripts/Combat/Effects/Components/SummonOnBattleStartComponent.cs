using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Скелеты-защитники» Некроманта (карточка [[the-bonewright]]): пассивка, которая один раз в начале
    /// боя выставляет носителю <see cref="_count"/> тел <see cref="_summon"/>. Статы призыва — из его
    /// собственного кита, умноженные на <c>SummonHealthEff</c>/<c>SummonDamageEff</c> призывателя.
    /// <para><b>Числа:</b> <c>_count</c> — сколько тел появляется на старте (Некромант = 2);
    /// <c>_diesWithSummoner</c> — уходят ли они вместе с хозяином. Сила самих тел живёт в их ките.</para>
    /// <para><b>Когда срабатывает:</b> ровно один раз, на первом тике боя.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему периодический компонент, а не <c>OnApply</c>:</b> пассивки накладываются в момент сборки
    /// юнита, когда арена ещё наполняется, а спавн посреди сборки менял бы список юнитов под ногами у
    /// того, кто его обходит. Первый тик — самая ранняя безопасная точка, и она же одинакова для обеих
    /// сторон.
    /// <para>Тела ставятся тем же швом, что и призыв способности (<c>ICombatContext.Summon</c>), поэтому
    /// подчиняются общим правилам: появляются со следующего тика, живут до конца боя и уходят с хозяином,
    /// если так задано.</para>
    /// </remarks>
    [Serializable]
    public sealed class SummonOnBattleStartComponent : IPeriodicComponent
    {
        [Tooltip("Кого выставить на старте боя (кит призыва — в этом ассете).")]
        [SerializeField] private UnitData _summon;

        [Tooltip("Сколько тел появляется. Некромант = 2.")]
        [Min(1)]
        [SerializeField] private int _count = 2;

        [Tooltip("Уходят ли тела вместе с призывателем.")]
        [SerializeField] private bool _diesWithSummoner;

        public float Interval => 1f / SimConstants.TickRate;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            if (eff.Counter != 0) return;   // стартовый отряд выставляется один раз за бой
            eff.Counter = 1;

            RuntimeUnit self = ctx.Target;
            if (_summon == null || self == null || self.IsDead) return;

            // Раскладка веером за спиной — та же, что у призыва способности, и живёт она в одном месте:
            // две копии формулы разъехались бы на первой правке, а зеркальность заметил бы только сторож.
            float step = Mathf.Max(0.6f, self.Stats.Get(StatType.Size));
            for (int i = 0; i < _count; i++)
            {
                Vector2 offset = SummonLayout.Offset(i, step, self);

                RuntimeUnit body = ctx.Combat.Summon(_summon, self.Team, self.Position + offset, self);
                if (body == null) return;   // призывать нечем (фабрика не подана) — это не боевая ошибка

                body.DiesWithSummoner = _diesWithSummoner;
            }
        }
    }
}
