using System;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Щит, который носитель зарабатывает СВОИМ уроном: доля нанесённого прямого удара уходит в
    /// <c>CurrentShield</c> («Стальной вихрь» Копейщика — 20% от урона вихря на 3 с). Реактивный:
    /// слушает <see cref="CombatEvent.DamageDealt"/> носителя.
    /// <para><b>Числа:</b> <c>_fraction</c> — доля нанесённого урона, уходящая в щит (0.2 = 20%).
    /// Длительность щита — длительность самого эффекта; сколько урона он поглотит, зависит только от
    /// того, сколько носитель успел нанести за это время.</para>
    /// <para><b>Почему реактив, а не число на касте:</b> фактический урон известен лишь после сведения
    /// тика (<c>TickLedger</c>) — броня, щиты и добивание меняют его. Считать долю от заявленного урона
    /// значило бы платить щитом за урон, которого не случилось.</para>
    /// <para><b>Когда срабатывает:</b> на каждом ПРЯМОМ уроне носителя (авто-атака или способность).
    /// Тики DoT и ответка шипов щит не растят: иначе один вихрь по толпе с горением кормил бы щит
    /// ещё секунды после самого удара.</para>
    /// <para><b>Готча:</b> накопленное живёт в <see cref="RuntimeEffect.HeldShield"/>, и по истечении
    /// снимается ровно оно. Пул общий с прочими щитами, поэтому поглощённую часть вычитать нельзя —
    /// кламп в ноль обязателен.</para>
    /// </summary>
    [Serializable]
    public sealed class DamageToShieldComponent : IReactiveComponent
    {
        [Tooltip("Доля нанесённого урона, уходящая в щит (0..1). «Стальной вихрь» = 0.2.")]
        [Range(0f, 1f)]
        [SerializeField] private float _fraction = 0.2f;

        public CombatEvent Events => CombatEvent.DamageDealt;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx)
        {
            if (ctx.Target == null) return;

            // Снимаем не больше, чем подняли: часть щита уже могли пробить. Отпускаем удерживаемое одним
            // глаголом — «прочитать и забыть обнулить» здесь невозможно.
            ctx.Target.CurrentShield = Mathf.Max(0f, ctx.Target.CurrentShield - ctx.Effect.ReleaseHeldShield());
        }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (!e.IsDirectHit) return;

            float gained = e.Amount * _fraction * ctx.Stacks;
            if (gained <= 0f) return;

            ctx.Target.CurrentShield += gained;
            ctx.Effect.AddHeldShield(gained);
        }
    }
}
