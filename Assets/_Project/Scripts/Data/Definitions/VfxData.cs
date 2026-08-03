using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Иммутабельное определение боевого VFX: id <c>vfx.*</c>, префаб и параметры спавна
    /// (размер / sorting / default-dir). Форма частиц — в префабе; слой и базовый order — здесь.
    /// Относительный order детей внутри префаба (Flash над Sparks) остаётся на префабе.
    /// </summary>
    /// <remarks>
    /// <b>Размер эффекта имеет ровно одного владельца — <see cref="SizeUnits"/> здесь.</b> До 31.07.2026
    /// их было четыре: <c>startSize</c> префаба, скрытая кривая <c>sizeOverLifetime</c>, множитель в этом
    /// SO и множитель силы удара из feel-конфига. Итог не читался ни из одного места, и боевая искра
    /// полгода была размером в два пикселя, хотя в инспекторе стояло 0.09 — кривая душила её в момент
    /// рождения. Теперь префаб задаёт ПРОПОРЦИИ частиц между собой, абсолют приходит отсюда, а
    /// единственный рантайм-множитель (сила удара) объявлен в feel-конфиге явным именем.
    /// Инвариант держит <c>VfxSizeContractTests</c>.
    /// Цена решения: превью частиц в инспекторе префаба показывает пропорции, а не боевой размер.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Vfx", fileName = "Vfx")]
    public sealed class VfxData : ContentDefinition
    {
        [Header("Prefab")]
        [Tooltip("Самодостаточный VFX-префаб (корневой PooledVfx + визуал).")]
        [SerializeField] private GameObject _prefab;

        [Header("Размер — единственный владелец")]
        [Tooltip("Размер САМОЙ КРУПНОЙ частицы эффекта в мировых единицах. 1 = метр, юнит-человек ~1.6, " +
                 "то есть 0.2 — заметная искра, 0.5 — вспышка в треть роста. Префаб задаёт пропорции " +
                 "частиц между собой; сколько эффект занимает на экране — решает это число и только оно.")]
        [SerializeField] private float _sizeUnits = 0.2f;

        [Header("Sorting (TagManager)")]
        [Tooltip("Sorting Layer: GroundFX (пыль у ног), OverheadFX (искры/muzzle/heal).")]
        [SerializeField] private string _sortingLayer = "OverheadFX";

        [Tooltip("Базовый sorting order эффекта. Дети префаба добавляют свой относительный order.")]
        [SerializeField] private int _sortingOrder;

        [Header("Spawn")]
        [Tooltip("Угол Z по умолчанию (град). Презентер может переопределить (напр. направление снаряда).")]
        [SerializeField] private float _defaultDirDeg;

        /// <summary>Префаб эффекта; null = эффект не спавнится.</summary>
        public GameObject Prefab => _prefab;

        /// <summary>
        /// Размер самой крупной частицы эффекта в мировых единицах. Единственный владелец размера:
        /// <see cref="Presentation.PooledVfx"/> приводит к нему пропорции префаба.
        /// </summary>
        public float SizeUnits => _sizeUnits;

        /// <summary>Имя sorting layer из TagManager.</summary>
        public string SortingLayerName => _sortingLayer;

        /// <summary>Базовый sorting order (дети префаба — относительно него).</summary>
        public int SortingOrder => _sortingOrder;

        /// <summary>Угол Z по умолчанию, градусы.</summary>
        public float DefaultDirDeg => _defaultDirDeg;
    }
}
