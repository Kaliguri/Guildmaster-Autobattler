using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// «Сосуд» — человек, носящий Мементо. Своих боевых статов у него нет: весь кит приходит от
    /// Мементо, а Сосуд несёт идентичность и нарратив (ГДД «Питч» §7, «Процедурный лор»).
    /// <para><b>Ассет — только для авторских Сосудов:</b> прегенов и сюжетных. Сосуды забега процедурные —
    /// они рождаются из сида и живут в сейве (досье = сид + индексы фрагментов, летопись переносится между
    /// забегами), поэтому ассетами не авторятся и в контент-БД не лежат. Пустая папка <c>Vessels/</c> — это
    /// «авторских ещё не написали», а не «слой мёртв».</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Vessel", fileName = "Vessel")]
    public sealed class VesselData : ContentDefinition
    {
        [Header("Identity")]
        [SerializeField] private string[] _tags;

        [Header("Fate")]
        [Tooltip("Статовая нагрузка Судьбы — то, чем авторский «Сосуд» отличается от процедурного.\n\n" +
                 "Это НЕ драфтовый перк «+»/«−»: тот выбирается игроком один из трёх и живёт в TraitData. " +
                 "И это не способ дать «Сосуду» собственные статы в обход Мементо — по ГДД он «обычный " +
                 "человек без своих статов», так что нагрузка Судьбы должна читаться как особенность " +
                 "личности, а не как второй стат-блок поверх кита.")]
        [SerializeField] private StatModifier[] _fateModifiers;

        public string[] Tags => _tags;

        /// <summary>Модификаторы Судьбы авторского «Сосуда» (см. поле — это не драфтовый перк).</summary>
        public StatModifier[] FateModifiers => _fateModifiers;
    }
}
