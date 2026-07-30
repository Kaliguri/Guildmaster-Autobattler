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

        [Tooltip("Сколько стаков даёт ОДНО наложение («Раздуть жар» кладёт сразу 5 углей). 1 = как обычно. " +
                 "Потолок MaxStacks всё равно не пробивается.")]
        [Min(1)]
        [SerializeField] private int _stacksPerApplication = 1;

        [Header("Dispel resistance")]
        [Tooltip("Снимается диспелом с DispelPower ≥ CleanseTier.")]
        [SerializeField] private int _cleanseTier;
        [Tooltip("Неснимаемо никаким диспелом.")]
        [SerializeField] private bool _unremovable;

        [Tooltip("Цена очистки в СТАКАХ по силе развеивания: [0] — диспел ровно этого тира, [1] — на уровень " +
                 "выше, [2] — на два и больше. Всё по нулям = эффект снимается целиком (обычное поведение).")]
        [SerializeField] private CleansePrice[] _cleansePrice = new CleansePrice[3];

        [Header("Behaviour")]
        [Tooltip("Полиморфные компоненты поведения (Combat-типы через SerializeReference). Шарятся между носителями — должны быть stateless.")]
        [SerializeReference] private IEffectComponent[] _components;

        [Header("Presentation / info")]
        [Tooltip("Телеграф: за сколько секунд ДО наложения этот эффект анонсируется показом (щит «Оплота» " +
                 "поднимается заранее). Работает благодаря лаге показа: сим уже посчитал наложение, а игрок " +
                 "его ещё не видел. 0 = без подводки, эффект просто появляется.")]
        [SerializeField, Range(0f, 1f)] private float _telegraphSeconds;

        [Tooltip("Иконка для бафф-бара HUD (опциональна: у скрытых/технических эффектов пустая).")]
        [SerializeField] private Sprite _icon;
        [Tooltip("Информационные теги для тултипов.")]
        [SerializeField] private TagData[] _infoTags;

        public EffectPolarity Polarity => _polarity;
        public EffectTag Tags => _tags;
        public float BaseDuration => _baseDuration;
        public StackRule Stacking => _stacking;
        public int MaxStacks => _maxStacks;

        /// <summary>
        /// Сколько стаков кладёт одно наложение (≥ 1). Позволяет выдать «сразу пять углей» одним
        /// применением, вместо пятикратного повтора всей нагрузки способности.
        /// </summary>
        /// <remarks>
        /// Живёт у эффекта, а не у способности: «сколько это стаков» — свойство самой порции углей, и
        /// одинаково для всех, кто её выдаёт. У способности же повтор нагрузки означает пять отдельных
        /// ударов — а вместе с ними пять диспелов и пять срабатываний всего остального, чего никто
        /// не заказывал.
        /// </remarks>
        public int StacksPerApplication => _stacksPerApplication < 1 ? 1 : _stacksPerApplication;
        public int CleanseTier => _cleanseTier;
        public bool Unremovable => _unremovable;

        /// <summary>Цена очистки: сколько стаков уносит одно развеивание данной силы.</summary>
        [System.Serializable]
        public struct CleansePrice
        {
            [Tooltip("Плоско — столько стаков за раз.")]
            public int Flat;

            [Tooltip("Доля от текущих стаков (0.25 = четверть). Берётся БОЛЬШЕЕ из двух.")]
            [Range(0f, 1f)]
            public float Pct;

            public bool IsEmpty => Flat <= 0 && Pct <= 0f;
        }

        /// <summary>
        /// Сколько стаков уносит ОДНО очищение силой <paramref name="dispelPower"/> (ГДД «Свойства эффекта»
        /// §Цена очистки, решения 2026-07-27/5 и /7). Цена растёт лестницей: диспел своего тира отщипывает,
        /// на уровень выше — заметно больше, на два — сносит половину и больше.
        /// </summary>
        /// <remarks>
        /// Нужно там, где стаки копятся без потолка («Угли»): иначе одна очистка стирала накопленное
        /// целиком и обесценивала ставку «долгий бой окупается» одним нажатием. Цена не задана — эффект
        /// снимается целиком, как было до этого решения (обычное поведение для нестакающихся).
        /// </remarks>
        public int CleanseStacks(int currentStacks, int dispelPower)
        {
            if (_cleansePrice == null || _cleansePrice.Length == 0) return currentStacks;

            // Насколько развеивание сильнее самого эффекта: 0 — вровень, 1 — на уровень выше, 2+ — с запасом.
            int overshoot = Mathf.Clamp(dispelPower - _cleanseTier, 0, _cleansePrice.Length - 1);
            CleansePrice price = _cleansePrice[overshoot];
            if (price.IsEmpty) return currentStacks; // цены нет — снимаем целиком

            int byPct = Mathf.CeilToInt(currentStacks * price.Pct);
            return Mathf.Clamp(Mathf.Max(price.Flat, byPct), 1, currentStacks);
        }
        public IEffectComponent[] Components => _components;
        /// <summary>
        /// За сколько секунд до наложения показ анонсирует этот эффект. Владелец числа — ассет: анимация
        /// и вспышка подстраиваются под него, а не наоборот (решение Макса 2026-07-29).
        /// </summary>
        public float TelegraphSeconds => _telegraphSeconds;

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
