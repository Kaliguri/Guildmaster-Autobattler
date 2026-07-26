using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Информационный тег для тултипов/фильтров (вики «13» §3.0): чисто UI-слой, НЕ путать с боевыми
    /// <see cref="EffectTag"/>-флагами. Имя/тултип — по конвенции <c>{id}.name</c>/<c>{id}.desc</c>.
    /// Авто-теги выводятся из данных при отображении; ручные теги живут в <c>InfoTags</c> контент-типов.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Tag", fileName = "Tag")]
    public sealed class TagData : ContentDefinition
    {
        [Tooltip("Ось тега — она же порядок чтения карточки.")]
        [SerializeField] private TagCategory _category = TagCategory.Role;

        [Tooltip("Иконка тега (опционально).")]
        [SerializeField] private Sprite _icon;

        public TagCategory Category => _category;
        public Sprite Icon => _icon;
    }
}
