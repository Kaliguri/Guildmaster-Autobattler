using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Иммутабельное определение таймированного эффекта: идентичность, полярность, категориальные
    /// теги, базовая длительность, правила стакинга/диспела и полиморфные компоненты поведения
    /// (вики «6» §5, «12» §2.1).
    /// </summary>
    /// <remarks>
    /// <see cref="Components"/> типизирован Data-маркером <see cref="IEffectComponent"/>, но реально
    /// хранит Combat-типы через <c>[SerializeReference]</c> (кросс-сборочный шов подтверждён спайком
    /// S1, вики «12» §5). Длительность — базовая в секундах; масштабирование эфф-эффектами и
    /// конверсия в тики — логика рантайма (<c>EffectSystem</c>, вики «11» §5).
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Effect", fileName = "Effect")]
    public sealed class EffectData : ContentDefinition
    {
        [Header("Identity")]
        [SerializeField] private EffectPolarity _polarity = EffectPolarity.Neutral;
        [Tooltip("Категориальные теги: диспел по категории, AI-фильтры, битовая маска на юните.")]
        [SerializeField] private EffectTag _tags;

        [Header("Timing")]
        [Tooltip("Базовая длительность, сек. 0 = мгновенный (один OnApply), -1 = постоянный (пассивка).")]
        [SerializeField] private float _baseDuration;

        [Header("Stacking")]
        [SerializeField] private StackRule _stacking = StackRule.None;
        [Tooltip("Потолок стаков (актуально для Stack/StackAndRefresh).")]
        [SerializeField] private int _maxStacks = 1;

        [Header("Dispel resistance")]
        [Tooltip("Снимается диспелом с DispelPower ≥ CleanseTier.")]
        [SerializeField] private int _cleanseTier;
        [Tooltip("Неснимаемо никаким диспелом.")]
        [SerializeField] private bool _unremovable;

        [Header("Behaviour")]
        [Tooltip("Полиморфные компоненты поведения (Combat-типы через SerializeReference). Шарятся между носителями — должны быть stateless.")]
        [SerializeReference] private IEffectComponent[] _components;

        [Header("Presentation / info")]
        [Tooltip("Иконка для бафф-бара HUD (опциональна: у скрытых/технических эффектов пустая).")]
        [SerializeField] private Sprite _icon;
        [Tooltip("Информационные теги для тултипов.")]
        [SerializeField] private TagData[] _infoTags;

        public EffectPolarity Polarity => _polarity;
        public EffectTag Tags => _tags;
        public float BaseDuration => _baseDuration;
        public StackRule Stacking => _stacking;
        public int MaxStacks => _maxStacks;
        public int CleanseTier => _cleanseTier;
        public bool Unremovable => _unremovable;
        public IEffectComponent[] Components => _components;
        public Sprite Icon => _icon;
        public TagData[] InfoTags => _infoTags;

        /// <summary>
        /// Собрать системный эффект в коде (не авторинг-ассет): для эффектов, которые движок создаёт сам,
        /// напр. маркер смещения «в полёте» (<see cref="EffectTag.KnockUp"/>). Так не заводим лишний .asset и
        /// не тащим ссылку на него в скоуп/тесты. Компоненты передаются напрямую (те же правила stateless).
        /// </summary>
        public static EffectData CreateRuntime(
            string id, EffectPolarity polarity, EffectTag tags, float baseDuration,
            bool unremovable, params IEffectComponent[] components)
        {
            var d = CreateInstance<EffectData>();
            d.SetId(id);
            d._polarity     = polarity;
            d._tags         = tags;
            d._baseDuration = baseDuration;
            d._stacking     = StackRule.None;
            d._maxStacks    = 1;
            d._cleanseTier  = 0;
            d._unremovable  = unremovable;
            d._components   = components ?? System.Array.Empty<IEffectComponent>();
            return d;
        }
    }
}
