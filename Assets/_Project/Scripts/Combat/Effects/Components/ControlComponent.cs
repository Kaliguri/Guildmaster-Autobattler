using System;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Контроль: запрещает действия носителю на время эффекта. Сами флаги
    /// (<c>CanAct/CanMove/CanCast</c>) пересчитывает <see cref="EffectSystem"/> из активных
    /// контрол-эффектов каждый тик — так перекрывающиеся контроли и независимое истечение
    /// разрешаются без счётчиков. Без diminishing returns (вики «6» §5.3).
    /// </summary>
    [Serializable]
    public sealed class ControlComponent : IRuntimeEffectComponent
    {
        [Tooltip("Оглушение/сон: нельзя ни атаковать, ни кастовать.")]
        [SerializeField] private bool _preventAct;

        [Tooltip("Корень/обездвиживание: нельзя двигаться.")]
        [SerializeField] private bool _preventMove;

        [Tooltip("Немота: нельзя кастовать способности.")]
        [SerializeField] private bool _preventCast;

        public bool PreventAct => _preventAct;
        public bool PreventMove => _preventMove;
        public bool PreventCast => _preventCast;

        // Флаги выставляет EffectSystem.RecomputeControl — компонент только хранит, что запрещает.
        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }
    }
}
