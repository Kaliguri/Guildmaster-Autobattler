using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Дуэль один на один</b> (разбойник-дуэлянт): носитель бьёт сильнее, пока рядом с ним <b>не больше
    /// одного противника</b>. В свалке прибавки нет — его сила в размене, а не в толпе.
    /// <para><b>Числа:</b> <c>_bonus</c> — прибавка долей (0.5 = +50%); <c>_radius</c> — радиус, в котором
    /// считаются противники; <c>_maxEnemies</c> — сколько их допускается для бонуса (1 = «только один»);
    /// <c>_autoAttackOnly</c> — считать только для авто-атак.</para>
    /// <para><b>Когда срабатывает:</b> в момент удара — окружение читается на каждый удар, поэтому
    /// подошедший второй враг гасит бонус сразу же, а ушедший возвращает.</para>
    /// </summary>
    /// <remarks>
    /// <b>Зеркало «Стаи», а не её копия.</b> <see cref="AllyProximityDamageBonusComponent"/> даёт
    /// прибавку ЗА каждого союзника и растёт с числом; здесь прибавка одна и включается ПОРОГОМ по
    /// врагам. Слить их в один компонент значило бы завести флаг «считать союзников или врагов» плюс
    /// флаг «за каждого или по порогу» — четыре режима в одном месте вместо двух ясных правил.
    /// <para><b>Считаем вокруг НОСИТЕЛЯ, а не вокруг цели:</b> дуэль — это про то, сколько врагов
    /// достают до него самого. При счёте вокруг цели дуэлянт получал бы бонус, вклинившись в одиночку
    /// в чужой строй, — ровно наоборот к замыслу.</para>
    /// <para><b>Цель в счёт входит:</b> тот, кого он бьёт, и есть первый противник рядом. Поэтому
    /// «только один» означает <c>_maxEnemies = 1</c>, а не ноль, и порог читается как в карточке.</para>
    /// <para><b>Буфер общий и одноразовый:</b> экземпляр компонента живёт в <see cref="EffectData"/> и
    /// делится всеми носителями, а симуляция однопоточная — список заполняется и тут же прочитывается
    /// внутри одного вызова, наружу не отдаётся.</para>
    /// </remarks>
    [Serializable]
    public sealed class DuelDamageBonusComponent : IOutgoingDamageBonusComponent
    {
        private static readonly List<RuntimeUnit> Nearby = new List<RuntimeUnit>(16);

        [Tooltip("Прибавка к урону, пока рядом не больше _maxEnemies противников: 0.5 = +50%.")]
        [SerializeField] private float _bonus = 0.5f;

        [Tooltip("Радиус, в котором считаются противники (мировые единицы).")]
        [Min(0.1f)]
        [SerializeField] private float _radius = 3f;

        [Tooltip("Сколько противников рядом ещё допускает бонус. 1 = «рядом только один враг».")]
        [Min(1)]
        [SerializeField] private int _maxEnemies = 1;

        [Tooltip("Только авто-атаки. Выкл = любой урон носителя, включая способности.")]
        [SerializeField] private bool _autoAttackOnly = true;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx)
        {
            if (_bonus == 0f) return 0f;
            if (_autoAttackOnly && !isAutoAttack) return 0f;
            if (attacker == null || ctx.Combat == null) return 0f;

            Nearby.Clear();
            ctx.Combat.QueryUnitsInRadius(attacker.Position, _radius, Nearby, TargetFilter.Enemies, attacker.Team);

            int enemies = 0;
            for (int i = 0; i < Nearby.Count; i++)
            {
                RuntimeUnit u = Nearby[i];
                if (u == null || u.IsDead) continue;
                enemies++;
            }
            Nearby.Clear();

            return enemies > 0 && enemies <= _maxEnemies ? _bonus : 0f;
        }
    }
}
