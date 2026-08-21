using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Последствие боя: травма или закалка (объединяет бывш. Injury/Mettle; GDD 9, вики «13» §3.2).
    /// Травма занимает слот своей <see cref="Grade"/>, снимается «Лечением» (<see cref="HealCostGold"/>)
    /// и — если мелкая — проходит сама; закалка постоянна и слотов не занимает.
    /// <para>Механика слотов и каскада — ГДД <c>injuries-mettle</c> §Травма, конкретный каталог из
    /// четырнадцати последствий — <c>injury-catalogue</c>. Логика переполнения живёт в
    /// <c>Guildmaster.Guild.InjuryCascade</c>: этот тип только описывает одно последствие.</para>
    /// </summary>
    /// <remarks>
    /// Раны обязаны быть <see cref="ModifierOp.PercentMult"/>, а не <c>PercentAdd</c>: три ушиба
    /// скорости при аддитивной операции дали бы −90% и бойца, стоящего на месте, тогда как
    /// перемножение трёх «−30%» оставляет 34% — тяжело, но играбельно.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Consequence", fileName = "Consequence")]
    public sealed class ConsequenceData : ContentDefinition
    {
        [SerializeField] private ConsequencePolarity _polarity = ConsequencePolarity.Injury;
        [Tooltip("Ступень травмы: в какой ряд слотов кладётся (3 мелких / 2 средних / 1 тяжёлая). " +
                 "У закалки не читается.")]
        [SerializeField] private InjuryGrade _grade = InjuryGrade.Bruise;
        [Tooltip("Модификаторы «Сосуду». Для ран — PercentMult, иначе несколько ран одного стата " +
                 "складываются в ноль.")]
        [SerializeField] private StatModifier[] _mods;
        [Tooltip("Для сложных (триггерных) последствий.")]
        [SerializeField] private EffectData[] _grantedEffects;
        [Tooltip("Через сколько ПРОЙДЕННЫХ узлов маршрута проходит само. 0 = не проходит никогда " +
                 "(средние, тяжёлые, закалка).")]
        [SerializeField] private int _expiresAfterNodes;
        [Tooltip("Цена снятия в «Лечении» (используется только при Injury).")]
        [SerializeField] private int _healCostGold;
        [Tooltip("Вес при выдаче внутри своей ступени.")]
        [SerializeField] private float _weight = 1f;
        [SerializeField] private Sprite _icon;
        [Tooltip("Информационные теги.")]
        [SerializeField] private TagData[] _infoTags;

        public ConsequencePolarity Polarity => _polarity;
        public InjuryGrade Grade => _grade;
        public StatModifier[] Mods => _mods;
        public EffectData[] GrantedEffects => _grantedEffects;

        /// <summary>Срок жизни в пройденных узлах; <c>0</c> — бессрочно (снимается только платно).</summary>
        public int ExpiresAfterNodes => _expiresAfterNodes;

        public int HealCostGold => _healCostGold;
        public float Weight => _weight;
        public Sprite Icon => _icon;
        public TagData[] InfoTags => _infoTags;

        /// <summary>Травма (занимает слот и снимается) — в отличие от закалки.</summary>
        public bool IsInjury => _polarity == ConsequencePolarity.Injury;
    }
}
