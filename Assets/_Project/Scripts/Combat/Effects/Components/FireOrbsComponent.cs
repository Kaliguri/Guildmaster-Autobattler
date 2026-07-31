using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Огненные сферы</b> (гоблин-маг): вокруг носителя крутятся сферы — на старте боя их полный запас,
    /// потраченные возвращаются по одной. Пока хоть одна доступна, маг бьёт быстрее, а его удар усилен и
    /// задевает площадь; каждая авто-атака расходует одну сферу.
    /// <para><b>Числа:</b> <c>_orbs</c> — запас (карточка: 3); <c>_rechargeSeconds</c> — за сколько
    /// возвращается ОДНА сфера (4, откатываются независимо); <c>_hasteBuff</c> — ускорение, пока сферы есть
    /// (его величина и длительность живут в самом бафе); <c>_orbStrike</c> — заряд удара со сферой (множитель
    /// и радиус — в нём).</para>
    /// <para><b>Когда срабатывает:</b> каждый тик — берёт сферу в руку (тратит заряд и взводит удар), пока
    /// взведённого нет, и подновляет ускорение.</para>
    /// </summary>
    /// <remarks>
    /// <b>Сферы — это заряды эффекта</b> (<c>RuntimeEffect.ArmCharges</c>): у них уже есть ровно нужная
    /// семантика «N штук, каждая перезаряжается сама, расход детерминирован». Свой счётчик означал бы второго
    /// владельца правила, которое блок и парирование уже соблюдают.
    /// <para><b>Ускорение — отдельный эффект, а не поле здесь.</b> Карточка обещает ФИКСИРОВАННУЮ прибавку,
    /// пока сфера есть, — а стакающийся <c>StatModifierComponent</c> умножился бы на число сфер и дал при
    /// трёх втрое больше обещанного. Поэтому баф короткий и подновляется тиком: сферы кончились — он гаснет
    /// сам, снимать его никому не нужно.</para>
    /// <para><b>Заряд взводится только когда его нет:</b> взводить каждый тик безопасно (множитель
    /// перезаписывается, а не копится), но тогда список эффектов носителя дёргался бы 30 раз в секунду.</para>
    /// </remarks>
    [Serializable]
    public sealed class FireOrbsComponent : IPeriodicComponent
    {
        [Tooltip("Запас сфер. Гоблин-маг = 3.")]
        [Min(1)]
        [SerializeField] private int _orbs = 3;

        [Tooltip("За сколько секунд возвращается ОДНА сфера (каждая откатывается независимо). Маг = 4.")]
        [Min(0.1f)]
        [SerializeField] private float _rechargeSeconds = 4f;

        [Tooltip("Ускорение, пока доступна хотя бы одна сфера. Величина и длительность — в самом бафе.")]
        [SerializeField] private EffectData _hasteBuff;

        [Tooltip("Заряд удара со сферой: множитель урона и радиус задевания живут в нём.")]
        [SerializeField] private EffectData _orbStrike;

        public float Interval => 1f / Core.Simulation.SimConstants.TickRate;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.ArmCharges(_orbs);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            bool armed = self.EmpowerDamageMult > 0f;

            // Ускорение держится, пока сфера либо уже в руке (заряд взведён), либо готова к взятию.
            if (_hasteBuff != null && (armed || HasReadyOrb(in ctx)))
                ctx.Combat.ApplyEffect(self, _hasteBuff, self);

            if (_orbStrike == null || armed) return;   // заряд уже взведён — второй раз не трогаем

            // Сфера тратится ЗДЕСЬ, вместе со взводом, а не по факту нанесённого удара. Так было
            // сначала — и давало на один усиленный удар больше, чем сфер: взвод шёл до удара, а расход
            // после него, поэтому четвёртый удар при трёх сферах всё ещё был усиленным (замер 2026-07-31).
            int recharge = Mathf.Max(1, Mathf.RoundToInt(_rechargeSeconds * Core.Simulation.SimConstants.TickRate));
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, recharge)) return;

            ctx.Combat.ApplyEffect(self, _orbStrike, self);
        }

        /// <summary>Есть ли сфера, готовая прямо сейчас — вопрос без расхода (нужен ускорению).</summary>
        private static bool HasReadyOrb(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            int now = ctx.Combat.CurrentTick;
            for (int i = 0; i < eff.ChargeCount; i++)
                if (eff.ChargeReadyTick(i) <= now) return true;

            return false;
        }
    }
}
