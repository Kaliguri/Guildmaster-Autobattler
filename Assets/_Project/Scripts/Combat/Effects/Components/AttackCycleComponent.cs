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
    /// <para><b>Когда срабатывает:</b> на ЗАВЕРШЁННОЙ Атаке носителя — взводит заряд СЛЕДУЮЩЕЙ. Комбо
    /// порвалось — гасит взведённое и начинает круг заново.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему не два <see cref="EveryNthAttackComponent"/>.</b> Тот умеет «каждый N-й», и два экземпляра
    /// с периодом 3 взвелись бы на одном и том же ударе: счётчики у них свои, но стартуют вместе. Цикл
    /// 1-2-3 — это ОДИН счётчик с несколькими выходами, поэтому и компонент один.
    /// <para><b>Взвод на предыдущей Атаке</b> — по той же причине, что у «каждой N-й»: усилить уже
    /// нанесённый удар нельзя, цифры снимаются до прилёта. Первая Атака серии поэтому всегда обычная, и
    /// это совпадает с замыслом («первый удар — обычный по одной цели»).</para>
    /// <para><b>Цикл отмеряет АТАКИ носителя</b> (<see cref="RuntimeUnit.ComboAttacks"/>), а не Удары и не
    /// события урона (вердикт Макса 2026-08-01). Событие урона приходит на каждого задетого, поэтому
    /// размашистая фаза по площади присылала их пачкой и проскакивала следующую: замером 2026-07-31 голем
    /// крутил первые две фазы и никогда не доходил до тяжёлого удара. Промах цикл ДВИГАЕТ — считается путь
    /// Атаки, а не результат.</para>
    /// <para><b>Разрыв Комбо гасит взведённый заряд.</b> Счётчик обнуляет сама серия, а вот заряд надо
    /// снять руками: он уже эффект на носителе. Снимаем по определению фазы, а не диспелом по тегу —
    /// иначе вместе с ним слетал бы чужой заряд от активки, у которого своё правило снятия.</para>
    /// </remarks>
    [Serializable]
    public sealed class AttackCycleComponent : IReactiveComponent
    {
        [Tooltip("Заряды по фазам цикла: длина массива = длина цикла, пустой элемент = обычный удар. " +
                 "Голем = [пусто, размашистый, тяжёлый].")]
        [SerializeField] private EffectData[] _phases;

        public CombatEvent Events => CombatEvent.AttackCompleted | CombatEvent.ComboBroken;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_phases == null || _phases.Length == 0) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            if (e.Type == CombatEvent.ComboBroken) { DisarmPhases(self, in ctx); return; }

            // Фаза СЛЕДУЮЩЕЙ Атаки: счётчик серии уже увеличен завершившейся, поэтому [0] достаётся
            // первой Атаке круга, [1] — второй, и цикл читается ровно так, как записан в ассете.
            EffectData charge = _phases[self.ComboAttacks % _phases.Length];
            if (charge == null) return;   // фаза без заряда — обычная Атака

            ctx.Combat.ApplyEffect(self, charge, self);
        }

        /// <summary>Снять заряд любой фазы, который остался висеть: круг начинается с начала.</summary>
        private void DisarmPhases(RuntimeUnit self, in EffectContext ctx)
        {
            for (int i = 0; i < _phases.Length; i++)
                if (_phases[i] != null) ctx.Combat.RemoveEffect(self, _phases[i]);
        }
    }
}
