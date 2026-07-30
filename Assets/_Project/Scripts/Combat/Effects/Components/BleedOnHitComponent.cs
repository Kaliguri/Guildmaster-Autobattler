using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Пассивка носителя: его удары <b>пускают кровь</b> — накладывают порцию кровотечения на задетую цель.
    /// Носитель — Десятина, навык «Жилы»: выпады в упор добавляют кровь сверху своего урона. Поток дальней
    /// формы кровь НЕ пускает — он и так бьёт типом «Кровотечение» напрямую (вердикт Макса 2026-07-30).
    /// <para><b>Числа:</b> <c>_bleed</c> — какой эффект накладывать (порционное Кровотечение);
    /// <c>_shareOfAttack</c> — доля урона удара, уходящая в кровь; <c>_autoAttackOnly</c> — реагировать
    /// только на авто-атаки, не на способности; <c>_requiredStance</c> — в какой форме навык жив.</para>
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
    /// правил, но доля принадлежит носителю: «30% от МОЕГО удара» невыразимо в ассете, общем на всех.
    /// Разложи это по отдельным ассетам крови — и правка линии перестанет доходить до половины
    /// носителей.</para>
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

        [Tooltip("Индекс боевой стойки, в которой кровь пускается. −1 = в любой стойке.")]
        [SerializeField] private int _requiredStance = -1;

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

            // Форма решает, пускает ли этот кит кровь: у Десятины кровью режут только выпады в упор,
            // поток же бьёт типом «Кровотечение» напрямую и DoT не вешает (вердикт Макса 2026-07-30).
            if (_requiredStance >= 0 && self.AttackStance != _requiredStance) return;

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
