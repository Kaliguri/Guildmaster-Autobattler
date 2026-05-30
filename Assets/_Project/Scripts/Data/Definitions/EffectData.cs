using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Определение таймированного эффекта: идентичность, полярность, теги, базовая длительность
    /// и полиморфные компоненты поведения (вики «10» §4.2).
    /// </summary>
    /// <remarks>
    /// СКЕЛЕТ Фазы 1: поля-швы. <see cref="Components"/> типизирован якорным <see cref="IEffectComponent"/>
    /// (см. его доку) — реальные компоненты и их применение появляются в Фазе 2.
    /// Длительность хранится как базовая; масштабирование эфф-эффектами и per-tick-rate DoT/HoT —
    /// логика рантайма (вики «11» §5–§5.1).
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Effect", fileName = "Effect")]
    public sealed class EffectData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _id;
        [SerializeField] private EffectPolarity _polarity = EffectPolarity.Neutral;
        [SerializeField] private string[] _tags;

        [Header("Timing")]
        [Tooltip("Базовая длительность, сек. ≤ 0 — мгновенный/перманентный (уточняется в Фазе 2).")]
        [SerializeField] private float _baseDuration;

        [Header("Behaviour (Фаза 2)")]
        [Tooltip("Полиморфные компоненты поведения. Реализации — Фаза 2 (Combat).")]
        [SerializeReference] private IEffectComponent[] _components;

        public string Id => _id;
        public EffectPolarity Polarity => _polarity;
        public string[] Tags => _tags;
        public float BaseDuration => _baseDuration;
        public IEffectComponent[] Components => _components;
    }
}
