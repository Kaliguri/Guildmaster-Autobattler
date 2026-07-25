using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Иммутабельное определение боевого VFX: id <c>vfx.*</c>, префаб и параметры спавна
    /// (scale / sorting / default-dir). Форма частиц — в префабе; слой и базовый order — здесь.
    /// Относительный order детей внутри префаба (Flash над Sparks) остаётся на префабе.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Vfx", fileName = "Vfx")]
    public sealed class VfxData : ContentDefinition
    {
        [Header("Prefab")]
        [Tooltip("Самодостаточный VFX-префаб (корневой PooledVfx + визуал).")]
        [SerializeField] private GameObject _prefab;

        [Tooltip("Множитель масштаба при спавне (1 = как в префабе).")]
        [SerializeField] private float _scale = 1f;

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

        /// <summary>Множитель масштаба при спавне.</summary>
        public float Scale => _scale;

        /// <summary>Имя sorting layer из TagManager.</summary>
        public string SortingLayerName => _sortingLayer;

        /// <summary>Базовый sorting order (дети префаба — относительно него).</summary>
        public int SortingOrder => _sortingOrder;

        /// <summary>Угол Z по умолчанию, градусы.</summary>
        public float DefaultDirDeg => _defaultDirDeg;
    }
}
