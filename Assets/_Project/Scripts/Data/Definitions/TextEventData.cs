using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Текстовый ивент карты в стиле Slay the Spire (план 11 §3.4, GDD 7 «Риск-ивент»): заголовок, тело,
    /// картинка и 2–4 варианта ответа, каждый — со списком последствий (<see cref="EventEffect"/>) для
    /// <see cref="RunState"/>. Полиморфен через <c>IEventFlow</c> (новый ивент = новый ассет, центр не трогаем).
    /// Домен id — <c>event</c> (см. ContentDomains).
    /// </summary>
    /// <remarks>
    /// Текста в ассете нет — только ключи локализации, выведенные из id ([[data-layer-principles]]):
    /// <c>{id}.title</c>, <c>{id}.body</c>, <c>{id}.choiceN.label</c>, <c>{id}.choiceN.result</c>.
    /// Строки живут в String Table <c>Content</c>, резолвит UI через <c>ILocalizationService</c>.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Text Event", fileName = "TextEvent")]
    public sealed class TextEventData : ContentDefinition
    {
        [Tooltip("Иллюстрация ивента (опц.).")]
        [SerializeField] private Sprite _image;

        [Tooltip("Варианты ответа (обычно 2–4). Каждый несёт свои последствия; тексты — по ключам из id.")]
        [SerializeField] private EventChoice[] _choices;

        public Sprite Image => _image;
        public IReadOnlyList<EventChoice> Choices => _choices ?? Array.Empty<EventChoice>();

        /// <summary>Ключ заголовка в таблице <c>Content</c>.</summary>
        public string TitleKey => Id + ".title";

        /// <summary>Ключ тела ивента.</summary>
        public string BodyKey => Id + ".body";

        /// <summary>Ключ подписи кнопки варианта <paramref name="index"/>.</summary>
        public string ChoiceLabelKey(int index) => $"{Id}.choice{index}.label";

        /// <summary>Ключ текста-результата варианта <paramref name="index"/> (может быть пустым в таблице).</summary>
        public string ChoiceResultKey(int index) => $"{Id}.choice{index}.result";
    }

    /// <summary>Вариант ответа в текстовом ивенте: последствия выбора. Тексты — по ключам из id ивента.</summary>
    [Serializable]
    public sealed class EventChoice
    {
        [Tooltip("Последствия выбора, применяются к RunState по порядку.")]
        [SerializeField] private EventEffect[] _effects;

        public IReadOnlyList<EventEffect> Effects => _effects ?? Array.Empty<EventEffect>();
    }

    /// <summary>Тип последствия выбора ивента (план 11 §5.1). Расширяется по мере узлов; центр — switch в applier.</summary>
    public enum EventEffectKind
    {
        Gold,              // Amount: изменить золото (+/-)
        GrantRelic,        // ContentId: выдать релик в запас (enforce вместимости §5.4)
        RemoveRelic,       // ContentId: убрать релик из запаса
        GainRelicCapacity, // Amount: изменить вместимость запаса реликов (+)
        GrantItem,         // ContentId: выдать предмет (проводка в бой — D1/позже; пока лог)
        Custom,            // Note: произвольное последствие (пока дебаг-лог; хук под будущие механики)
    }

    /// <summary>Одно последствие выбора ивента (данные). Интерпретация — <c>EventEffectApplier</c>.</summary>
    [Serializable]
    public sealed class EventEffect
    {
        [SerializeField] private EventEffectKind _kind;

        [Tooltip("Числовой аргумент (золото / вместимость).")]
        [SerializeField] private int _amount;

        [Tooltip("Content id аргумента (relic.* / item.*).")]
        [SerializeField] private string _contentId;

        [Tooltip("Заметка для Custom/лога (что должно произойти).")]
        [SerializeField, TextArea(1, 3)] private string _note;

        public EventEffectKind Kind => _kind;
        public int Amount => _amount;
        public string ContentId => _contentId;
        public string Note => _note;
    }
}
