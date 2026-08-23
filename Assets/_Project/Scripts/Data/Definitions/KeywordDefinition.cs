using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Раздел глоссария — по нему keyword группируются в справочнике (§II.10.7).</summary>
    public enum KeywordCategory
    {
        /// <summary>Статус на юните: горение, яд, замедление.</summary>
        Status = 0,

        /// <summary>Урон и его виды: физический, магический, чистый.</summary>
        Damage = 1,

        /// <summary>Защита: броня, щит, сопротивление.</summary>
        Defense = 2,

        /// <summary>Поведение в бою: угроза, стелс, метка.</summary>
        Behaviour = 3,

        /// <summary>Понятия забега: мементо, сосуд, узел.</summary>
        Run = 4,

        Other = 5,
    }

    /// <summary>
    /// Ключевое слово глоссария: термин, который упоминается в описаниях и объясняется вложенной
    /// подсказкой (план §II.10.5 п.4, §II.10.7).
    /// </summary>
    /// <remarks>
    /// Тексты здесь НЕ лежат — они в таблице <c>Content</c> по конвенции ключей
    /// (<c>{id}.name</c> + падежные формы, <c>{id}.desc</c> краткое, <c>{id}.desc.full</c> полное).
    /// Так термин переводится, как всякий контент, и не превращается в русский литерал внутри ассета.
    /// <para>Два описания заводятся СРАЗУ (краткое для подсказки, полное для статьи): справочник
    /// придёт позже, но добавлять второй текст к сотне уже написанных терминов — отдельная работа,
    /// которой не будет, если поле есть с самого начала.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Keyword", fileName = "Keyword")]
    public sealed class KeywordDefinition : ContentDefinition
    {
        [Tooltip("Раздел глоссария — группировка в справочнике.")]
        [SerializeField] private KeywordCategory _category = KeywordCategory.Other;

        [Tooltip("Иконка термина (опционально): статус-эффекты показывают её в подсказке.")]
        [SerializeField] private Sprite _icon;

        [Tooltip("Связанный контент (эффект/способность), если термин описывает конкретную сущность игры.")]
        [SerializeField] private ContentDefinition _source;

        public KeywordCategory Category => _category;
        public Sprite Icon => _icon;
        public ContentDefinition Source => _source;
    }
}
