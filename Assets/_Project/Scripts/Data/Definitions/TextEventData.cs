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
    /// Тексты сейчас хранятся напрямую (MVP/дебаг). TODO(loc): перевести Title/Body/Choice.Label на лок-ключи
    /// через String Tables ([[data-layer-principles]]) — поля станут ключами, резолв в UI.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Text Event", fileName = "TextEvent")]
    public sealed class TextEventData : ContentDefinition
    {
        [Tooltip("Заголовок ивента (пока прямой текст; TODO лок-ключ).")]
        [SerializeField] private string _title;

        [Tooltip("Тело ивента — описание ситуации (пока прямой текст; TODO лок-ключ).")]
        [SerializeField, TextArea(3, 10)] private string _body;

        [Tooltip("Иллюстрация ивента (опц.).")]
        [SerializeField] private Sprite _image;

        [Tooltip("Варианты ответа (обычно 2–4). Каждый несёт свои последствия.")]
        [SerializeField] private EventChoice[] _choices;

        public string Title => _title;
        public string Body => _body;
        public Sprite Image => _image;
        public IReadOnlyList<EventChoice> Choices => _choices ?? Array.Empty<EventChoice>();
    }

    /// <summary>Вариант ответа в текстовом ивенте: подпись кнопки, опц. текст-результат и последствия.</summary>
    [Serializable]
    public sealed class EventChoice
    {
        [Tooltip("Подпись кнопки выбора (пока прямой текст; TODO лок-ключ).")]
        [SerializeField] private string _label;

        [Tooltip("Текст-результат после выбора (опц.): что произошло.")]
        [SerializeField, TextArea(2, 5)] private string _resultText;

        [Tooltip("Последствия выбора, применяются к RunState по порядку.")]
        [SerializeField] private EventEffect[] _effects;

        public string Label => _label;
        public string ResultText => _resultText;
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
