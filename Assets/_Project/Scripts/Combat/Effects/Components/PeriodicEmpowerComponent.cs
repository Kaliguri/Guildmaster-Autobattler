using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Восходящий удар» Монаха воды (карточка [[the-torrent]]): пассивка, которая раз в
    /// <see cref="Interval"/> секунд выдаёт носителю <see cref="_charge"/> — заряд усиленной авто-атаки.
    /// Сам заряд (множитель урона, толчок, чем снимается) живёт в том эффекте, а не здесь.
    /// <para><b>Числа:</b> <c>_periodSeconds</c> — как часто взводится заряд (Монах воды = 4). Больше
    /// у компонента нет ничего намеренно: он таймер, а не сила.</para>
    /// <para><b>Когда срабатывает:</b> каждые <c>_periodSeconds</c>, пока висит пассивка. Заряд не
    /// копится: повторная выдача поверх невытраченного просто взводит его заново.</para>
    /// </summary>
    /// <remarks>
    /// <b>Период фиксированный и от скорости атаки не зависит</b> — решение карточки: иначе у факта «как
    /// часто он толкает» стало бы два владельца (этот таймер и стат <c>AttackSpeed</c>), и разгон темпа
    /// незаметно превращал бы позиционный трюк в основной источник урона.
    /// </remarks>
    [Serializable]
    public sealed class PeriodicEmpowerComponent : IPeriodicComponent
    {
        [Tooltip("Период выдачи заряда, сек. Монах воды = 4.")]
        [SerializeField] private float _periodSeconds = 4f;

        [Tooltip("Эффект-заряд, выдаваемый носителю (усиление следующей авто-атаки).")]
        [SerializeField] private EffectData _charge;

        public float Interval => _periodSeconds > 0f ? _periodSeconds : 4f;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (_charge == null || self == null || self.IsDead) return;

            ctx.Combat.ApplyEffect(self, _charge, self);
        }
    }
}
