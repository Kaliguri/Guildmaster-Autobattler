using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Пул фрагментов, из которых рождается процедурный «Сосуд»: имя и прозвище. Данные, а не строки в
    /// коде — имена это нарратив, и владелец у них человек, а не фабрика
    /// (ГДД <c>60-narrative</c>, <c>procedural-lore</c>).
    /// </summary>
    /// <remarks>
    /// <b>Фрагменты, а не готовые имена:</b> досье «Сосуда» разворачивается из сида рождения, и сид
    /// обязан выбирать части независимо — иначе два человека с соседними сидами оказались бы полными
    /// тёзками. Хранится в сейве при этом только сид, а не собранная строка
    /// (<c>VesselState.BirthSeed</c>): правка пула должна доезжать до уже нанятых.
    /// <para><b>Пустой пул — это незаполненный ассет, а не «имён не бывает».</b> Фабрика в таком случае
    /// кричит в лог и отдаёт технический ярлык, чтобы отсутствие контента было видно сразу.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Vessel Name Pool", fileName = "VesselNamePool")]
    public sealed class VesselNamePool : ScriptableObject
    {
        [Tooltip("Личные имена. Заготовка: окончательный список — за нарративом.")]
        [SerializeField] private string[] _names;

        [Tooltip("Прозвища и происхождение: «из каменоломни», «Тихий». Могут быть пустыми — тогда " +
                 "человек зовётся одним именем.")]
        [SerializeField] private string[] _epithets;

        public string[] Names => _names;
        public string[] Epithets => _epithets;

        /// <summary>Есть ли из чего собирать имя.</summary>
        public bool IsEmpty => _names == null || _names.Length == 0;
    }
}
