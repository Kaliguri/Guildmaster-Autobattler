using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Пассивка носителя: его удары <b>пускают кровь</b> — накладывают порцию кровотечения на задетую цель.
    /// Носитель — Десятина: в дальней форме кровью идёт весь её урон, в ближней кровь добавляется к выпадам.
    /// <para><b>Числа:</b> <c>_bleed</c> — какой эффект накладывать (порционное Кровотечение);
    /// <c>_autoAttackOnly</c> — реагировать только на авто-атаки, не на способности.</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.DamageDealt"/> носителя по чужой цели.</para>
    /// </summary>
    /// <remarks>
    /// <b>Доля считается от урона удара ПО СТАТАМ, а не от прошедшего сквозь броню</b> (вердикт Макса
    /// 2026-07-30, формулировка карточки — «до учёта защитных показателей»). Причина не в удобстве: тик
    /// кровотечения сам режется физбронёй, поэтому доля от уже прошедшего урона означала бы, что одна и та
    /// же броня применена дважды — против танка кровь потеряла бы три четверти силы. И наоборот: при
    /// расчёте от статов броня цели читается ЖИВОЙ на каждом тике, а значит сорванная с танка защита
    /// немедленно делает кровь больнее, даже если ранивший уже мёртв.
    /// <para><b>Величину несёт наложение, а не ассет крови.</b> У линии один эффект и один владелец
    /// правил, но доля у каждой формы своя: поток отдаёт кровью весь свой урон, выпады добавляют её
    /// сверху долей. Разложи это по отдельным ассетам крови — и правка линии перестанет доходить до
    /// половины носителей.</para>
    /// </remarks>
    [Serializable]
    public sealed class BleedOnHitComponent : IReactiveComponent
    {
        [Tooltip("Эффект кровотечения (порционный, StackRule.Portions).")]
        [SerializeField] private EffectData _bleed;

        [Tooltip("Доля урона удара, уходящая в кровь: 0.3 = «сверху 30% от урона своей атаки», " +
                 "1 = «весь урон приходит кровотечением» (поток Десятины).")]
        [SerializeField] private float _shareOfAttack = 0.3f;

        [Tooltip("Только авто-атаки: способности крови не пускают. Тики DoT и ответки реактивов " +
                 "исключены всегда — иначе кровь порождала бы кровь.")]
        [SerializeField] private bool _autoAttackOnly = true;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_bleed == null) return;

            // Прямой удар и только он: тик DoT и ответка реактива кровь не пускают, иначе кровотечение
            // подливало бы само себя и росло бы без потолка (которого у линии нет по решению).
            if (_autoAttackOnly ? !e.IsAutoAttack : !e.IsDirectHit) return;

            RuntimeUnit self = ctx.Target;   // DamageDealt доставляется НАНЁСШЕМУ урон
            if (self == null || self.IsDead) return;

            RuntimeUnit victim = e.Target;
            if (victim == null || victim.IsDead || victim == self) return;

            // Урон удара по статам — тот самый «до учёта защитных показателей». Берём стат, а не
            // e.Amount: e.Amount — это уже прошедшее сквозь броню, и кровь получила бы её вычет второй раз.
            float attack = self.Stats.Get(StatType.AutoAttackDamage);
            float portion = attack * _shareOfAttack;
            if (portion <= 0f) return;

            ctx.Combat.ApplyEffect(victim, _bleed, self, durationSeconds: 0f, potency: portion);
        }
    }
}
