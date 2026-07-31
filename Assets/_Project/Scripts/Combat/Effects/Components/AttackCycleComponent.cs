using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Цикл ударов</b> (земляной голем): удары носителя идут по кругу, и каждой фазе цикла свой заряд —
    /// первый удар обычный, второй размашистый, третий тяжёлый, затем заново. Что делает фаза, живёт в её
    /// заряде: множитель, площадь, отброс, довесок эффектов.
    /// <para><b>Числа:</b> <c>_phases</c> — заряды по фазам, длина массива и есть длина цикла. Пустой
    /// элемент = обычный удар этой фазы. Голем: <c>[null, размашистый, тяжёлый]</c>.</para>
    /// <para><b>Когда срабатывает:</b> на нанесённой авто-атаке носителя — взводит заряд СЛЕДУЮЩЕГО удара.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему не два <see cref="EveryNthAttackComponent"/>.</b> Тот умеет «каждый N-й», и два экземпляра
    /// с периодом 3 взвелись бы на одном и том же ударе: счётчики у них свои, но стартуют вместе. Цикл
    /// 1-2-3 — это ОДИН счётчик с несколькими выходами, поэтому и компонент один.
    /// <para><b>Взвод на предыдущем ударе</b> — по той же причине, что у «каждой N-й»: усилить уже
    /// нанесённый удар нельзя, цифры снимаются до прилёта. Первый удар боя поэтому всегда обычный, и это
    /// совпадает с замыслом («первый удар — обычный по одной цели»).</para>
    /// <para><b>Промах цикл не двигает:</b> событие приходит только на состоявшийся урон. Иначе противник
    /// мог бы «съедать» тяжёлый удар уклонениями — та же логика, что у <c>EveryNthAttackComponent</c>.</para>
    /// </remarks>
    [Serializable]
    public sealed class AttackCycleComponent : IReactiveComponent
    {
        [Tooltip("Заряды по фазам цикла: длина массива = длина цикла, пустой элемент = обычный удар. " +
                 "Голем = [пусто, размашистый, тяжёлый].")]
        [SerializeField] private EffectData[] _phases;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_phases == null || _phases.Length == 0 || !e.IsAutoAttack) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            RuntimeEffect eff = ctx.Effect;
            eff.Counter++;

            // Фаза СЛЕДУЮЩЕГО удара: счётчик уже учёл только что нанесённый.
            EffectData charge = _phases[eff.Counter % _phases.Length];
            if (charge == null) return;   // фаза без заряда — обычный удар

            ctx.Combat.ApplyEffect(self, charge, self);
        }
    }
}
