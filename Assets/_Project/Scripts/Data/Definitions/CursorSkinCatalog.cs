using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Набор скинов курсора: что вообще можно надеть и в каком порядке это показывать.
    /// </summary>
    /// <remarks>
    /// <b>Зачем каталог, если есть общий реестр контента.</b> Реестр отвечает на вопрос «какой ассет
    /// стоит за этим id» и наполняется синком по всему проекту — порядка и состава набора он не знает.
    /// Витрине же нужно и то, и другое: какие скины показать, в каком порядке и какой считается
    /// умолчанием. Каталог хранит ровно это, а id остаются каноническими (<c>cursor.*</c>) и по-прежнему
    /// принадлежат <see cref="ContentDefinition"/>.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Cursor Skin Catalog", fileName = "CursorSkinCatalog")]
    public sealed class CursorSkinCatalog : ScriptableObject
    {
        [Tooltip("Скины в порядке показа в витрине. Первый по умолчанию, если умолчание не задано.")]
        [SerializeField] private List<CursorSkinData> _skins = new List<CursorSkinData>();

        [Tooltip("Скин новичка: он же подставляется, когда сохранённый скин не найден.")]
        [SerializeField] private CursorSkinData _default;

        /// <summary>Все скины набора, в порядке показа.</summary>
        public IReadOnlyList<CursorSkinData> Skins => _skins;

        /// <summary>
        /// Умолчание набора. Если поле не заполнено — первый в списке: пустой курсор хуже любого
        /// выбранного за игрока.
        /// </summary>
        public CursorSkinData Default => _default != null ? _default : (_skins.Count > 0 ? _skins[0] : null);

        /// <summary>
        /// Скин по id. Неизвестный id отдаёт умолчание: id приходит из чужого профиля и по сети, то
        /// есть это внешний вход, а курсора у игрока не быть не может.
        /// </summary>
        public CursorSkinData Resolve(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < _skins.Count; i++)
                {
                    if (_skins[i] != null && _skins[i].Id == id) return _skins[i];
                }
            }

            return Default;
        }
    }
}
