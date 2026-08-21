using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Маскировка</b> (дизайн Макса 2026-07-30, числа и правила 2026-07-31): пока эффект висит,
    /// носитель скрыт от вражеского выбора цели — до тех пор, пока враг не подойдёт ближе радиуса его
    /// ступени. Радиусы общие на игру (<c>SimTuningConfig</c>, вкладка «Маскировка»), здесь живёт
    /// только <see cref="Tier"/> — какой ступенью маскируется этот кит.
    /// </summary>
    /// <remarks>
    /// <b>Что делает маскировка, тремя правилами:</b>
    /// <list type="number">
    /// <item>скрытого не выбирают целью — фильтр стоит в единственной точке выбора
    /// (<c>ProfileBrain.SelectBest</c>), и это редкая удача: правило нигде не размножено;</item>
    /// <item><b>удар по скрытому гаснет</b> — «если его пытаются ударить и он уходит в инвиз, это
    /// уклонение и должно работать как уклонение» (Макс). Случай краевой: враг занёс удар, пока цель
    /// была видна, и потерял её из виду до контакта;</item>
    /// <item>своя атака или каст маскировку снимают — этим занимаются <c>AutoAttackSystem</c> и
    /// <c>AbilitySystem</c>, диспелом по тегу <see cref="EffectTag.Stealth"/>. Правило снятия живёт
    /// там же, где действие, иначе каждый новый источник маскировки пришлось бы учить ему заново.</item>
    /// </list>
    /// <para><b>Ступень, а не радиус, в данных кита:</b> «насколько хорошо прячется» — свойство юнита,
    /// «с какого расстояния его видно» — балансная ручка, которую крутят на всю игру разом.</para>
    /// <para><b>Инвиз — это ступень, а не отдельная механика</b> (вердикт Макса 2026-07-31). Сегодняшняя
    /// «Скрытность» Убийцы становится её потребителем: тот же бафф урона и скорости плюс
    /// <see cref="ConcealmentTier.Invisible"/>.</para>
    /// </remarks>
    [Serializable]
    public sealed class ConcealmentComponent : IPreDamageComponent
    {
        [Tooltip("Ступень маскировки. Радиус обнаружения берётся по ней из SimTuningConfig; Инвиз не " +
                 "обнаруживается расстоянием вовсе и снимается только своим действием.")]
        [SerializeField] private ConcealmentTier _tier = ConcealmentTier.Medium;

        /// <summary>Ступень этого источника. Читает <c>ConcealmentSystem</c>, собирая сильнейшую из активных.</summary>
        public ConcealmentTier Tier => _tier;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        /// <summary>Выше отхода намеренно: маскировка НИЧЕГО не тратит, и спрашивать её надо прежде платных негейтов — иначе заряд отхода сгорит на ударе, который и так не нашёл бы цель.</summary>

        public int Priority => ReactionPriority.Evade + 10;


        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || !self.IsHidden) return;

            // Гасим только ПРЯМОЙ удар: тики яда и ответка шипов бьют по площади или по факту, а не по
            // выбранной цели, — прятаться от них нечего, и иначе маскировка стала бы иммунитетом к DoT.
            if (!incoming.IsDirectHit) return;

            result.Negated = true;
        }
    }
}
