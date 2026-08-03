using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Стая бьёт злее</b> (волк стаи): носитель наносит больше урона за КАЖДОГО живого союзника в
    /// радиусе вокруг себя. Одинокий волк слаб, три волка вместе — угроза; сила фракции зверей
    /// приходит из числа, а не из статов отдельной особи.
    /// <para><b>Числа:</b> <c>_bonusPerAlly</c> — прибавка за одного союзника долей (0.15 = +15%);
    /// <c>_radius</c> — радиус в мировых единицах, в котором союзник считается «рядом»;
    /// <c>_maxAllies</c> — сколько союзников максимум идёт в счёт (0 = без потолка);
    /// <c>_autoAttackOnly</c> — считать только для авто-атак.</para>
    /// <para><b>Когда срабатывает:</b> в момент удара — состав стаи читается на каждый удар, поэтому
    /// смерть союзника ослабляет волка сразу же, а не по истечении бафа.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему компонент, а не аура-каст.</b> Тот же бонус выражался бы способностью
    /// <c>AlliesInRadius</c>, накладывающей стакающийся баф, — и был бы неверен дважды: баф не гаснет в
    /// момент смерти союзника (живёт до истечения длительности), а кастующий по контракту
    /// <c>ApplyAllyAura</c> бафает себя, из-за чего одинокий волк получал бы бонус «за самого себя».
    /// Чтение состава в момент удара избавляет от обоих расхождений.
    /// <para><b>Носитель в счёт не идёт:</b> запрос по радиусу возвращает и его самого (см.
    /// <c>ApplyAllyAura</c>, где для этого держат флаг <c>casterIncluded</c>), поэтому он вычитается
    /// явно. Иначе «за союзника» читалось бы как «за союзника плюс один».</para>
    /// <para><b>Буфер общий и одноразовый:</b> экземпляр компонента живёт в <c>EffectData</c> и
    /// разделяется всеми носителями, а симуляция однопоточная — список заполняется и тут же
    /// прочитывается внутри одного вызова, наружу не отдаётся.</para>
    /// </remarks>
    [Serializable]
    public sealed class AllyProximityDamageBonusComponent : IOutgoingDamageBonusComponent
    {
        private static readonly List<RuntimeUnit> Nearby = new List<RuntimeUnit>(16);

        [Tooltip("Прибавка к урону за КАЖДОГО союзника в радиусе долей: 0.15 = +15%.")]
        [SerializeField] private float _bonusPerAlly = 0.15f;

        [Tooltip("Радиус «рядом», мировые единицы.")]
        [Min(0.1f)]
        [SerializeField] private float _radius = 3f;

        [Tooltip("Сколько союзников максимум идёт в счёт. 0 = без потолка.")]
        [Min(0)]
        [SerializeField] private int _maxAllies;

        [Tooltip("Только авто-атаки. Выкл = любой урон носителя, включая способности.")]
        [SerializeField] private bool _autoAttackOnly = true;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx)
        {
            if (_bonusPerAlly == 0f) return 0f;
            if (_autoAttackOnly && !isAutoAttack) return 0f;
            if (attacker == null || ctx.Combat == null) return 0f;

            Nearby.Clear();
            ctx.Combat.QueryUnitsInRadius(attacker.Position, _radius, Nearby, TargetFilter.Allies, attacker.Team);

            int allies = 0;
            for (int i = 0; i < Nearby.Count; i++)
            {
                RuntimeUnit u = Nearby[i];
                if (u == null || u == attacker || u.IsDead) continue;
                allies++;
            }
            Nearby.Clear();

            if (allies <= 0) return 0f;
            if (_maxAllies > 0 && allies > _maxAllies) allies = _maxAllies;
            return _bonusPerAlly * allies;
        }
    }
}
