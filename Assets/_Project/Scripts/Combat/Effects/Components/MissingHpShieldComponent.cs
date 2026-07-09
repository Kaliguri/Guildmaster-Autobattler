using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Щит с величиной, зависящей от текущего HP носителя: <c>flat + pct × недостающее HP</c>
    /// (§9.3, «Оплот» Защитника — тем толще, чем сильнее просел). Величина считается в момент
    /// наложения (не из статов — недостающее HP юнит-относительно) и запоминается в
    /// <see cref="RuntimeEffect.PendingShield"/>, чтобы <c>OnExpire</c> снял ровно её.
    /// </summary>
    [Serializable]
    public sealed class MissingHpShieldComponent : IRuntimeEffectComponent
    {
        [Tooltip("Плоская часть щита.")]
        [SerializeField] private float _flat = 20f;

        [Tooltip("Доля недостающего HP цели (0..1), добавляемая к щиту. Защитник = 0.15.")]
        [SerializeField] private float _pctMissingHp = 0.15f;

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit t = ctx.Target;
            if (t?.Stats == null) return;

            float missing = t.Stats.Get(StatType.MaxHP) - t.CurrentHP;
            if (missing < 0f) missing = 0f;

            float amount = (_flat + _pctMissingHp * missing) * ctx.Stacks;
            ctx.Effect.PendingShield = amount;
            t.CurrentShield += amount;
        }

        public void OnExpire(in EffectContext ctx)
        {
            // Снимаем не больше, чем подняли (часть могла быть поглощена уроном).
            if (ctx.Target == null) return;
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield - ctx.Effect.PendingShield);
        }
    }
}
