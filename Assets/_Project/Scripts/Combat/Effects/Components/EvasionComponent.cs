using System;
using Guildmaster.Combat.Effects;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Уклонение</b> (каталог стат-модификаторов): зеркало Слепоты со стороны ЦЕЛИ. По носителю бьют,
    /// но <b>каждая X-я атака уходит мимо</b> — не «с шансом». Глубина задаётся стаками: один стак
    /// отправляет мимо одну атаку из четырёх, четыре стака — все четыре.
    /// <para><b>Числа:</b> <c>_periodAtOneStack</c> — из скольких атак промахивается одна при ОДНОМ стаке
    /// (4 = каждая четвёртая). Каждый следующий стак сокращает период на единицу, до одной атаки. Больше
    /// ничего здесь нет намеренно: «насколько скользкий» решают стаки источника, а не своё поле у каждого
    /// носителя — ровно как у <see cref="BlindComponent"/>.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage по счётчику полученных Ударов
    /// (<see cref="RuntimeUnit.HitsTaken"/>), только по авто-атакам. Урон способностей, DoT и шипов не
    /// гасит: уклоняются от взмаха, а не от горения.</para>
    /// </summary>
    /// <remarks>
    /// <b>Не путать с «Отходом»</b> (<see cref="DodgeComponent"/>). Тот тратит заряд, уносит носителя с
    /// места и отменяет удар целиком; этот ничего не тратит и просто отправляет чужую атаку мимо. До
    /// 2026-08-21 слово «Уклонение» носил именно отход — переименование освободило имя (`2026-08-21/3`).
    /// <para><b>Дееспособности не требует:</b> уклонение — не действие, а свойство носителя в эти
    /// секунды. Оглушённый под ним по-прежнему скользкий, как ослеплённый по-прежнему слеп.</para>
    /// <para><b>Счёт по ударам ЮНИТА, а не по счётчику эффекта:</b> порций может быть несколько, и у
    /// каждой свой <c>Counter</c> — период считался бы от разных начал, и «каждая четвёртая» превратилась
    /// бы в «когда как». Та же причина, что у слепоты.</para>
    /// <para><b>Приоритет выше отхода</b> намеренно: уклонение бесплатно, и спрашивать его надо прежде
    /// платных негейтов — иначе заряд отхода сгорел бы на ударе, который и так уходил мимо.</para>
    /// </remarks>
    [Serializable]
    public sealed class EvasionComponent : IPreDamageComponent, IStackableComponent
    {
        [Tooltip("Из скольких атак по носителю уходит мимо одна при ОДНОМ стаке (4 = каждая четвёртая). " +
                 "Каждый следующий стак сокращает период на 1, минимум — каждая атака.")]
        [Min(1)]
        [SerializeField] private int _periodAtOneStack = 4;

        /// <summary>Бесплатный негейт — спрашивается прежде платных, чтобы те не жгли заряды впустую.</summary>
        public int Priority => ReactionPriority.Evade + 10;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Стаки читаются в момент удара, состояния между ударами компонент не держит — значит и
            // рестак ничего не пересчитывает (как у слепоты).
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (!incoming.IsAutoAttack) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            int stacks = ctx.Stacks;
            if (stacks <= 0) return;

            int period = _periodAtOneStack - (stacks - 1);
            if (period < 1) period = 1;

            // Считаем по УЖЕ принятым Ударам: счётчик инкрементируется до опроса, поэтому первый удар
            // под уклонением — это HitsTaken == 1, и при периоде 4 мимо уходит четвёртый.
            if (self.HitsTaken % period != 0) return;

            result.Negated = true;
        }
    }
}
