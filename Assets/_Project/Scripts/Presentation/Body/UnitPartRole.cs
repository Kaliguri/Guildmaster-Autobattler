using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Метка роли на узле части скелетного тела: оружие / щит / конечность. Читается
    /// <see cref="SkeletalBodyVisual"/> при кэшировании частей и решает, светится ли часть на касте
    /// (маска <see cref="BodyVisualState.GlowRoles"/>). Нет метки — часть считается <see cref="PartRole.Body"/>
    /// и на касте не светится. Ставится на префабе: обычно одна оружейная часть на юнита.
    /// <para>Роль живёт на УЗЛЕ, а не списком в теле: она принадлежит части (клинку), а не порядку в
    /// <c>SkeletalBodyVisual._parts</c>, и переживает пересборку списка частей.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitPartRole : MonoBehaviour
    {
        [Tooltip("Что это за часть. Weapon (меч/посох/лук/коготь), Shield, Limb (кулак/нога для безоружных " +
                 "ударов). Body — обычная часть, на касте не светится.")]
        [SerializeField] private PartRole _role = PartRole.Body;

        /// <summary>Роль части. Определяет, входит ли часть в маску свечения приёма.</summary>
        public PartRole Role => _role;
    }
}
