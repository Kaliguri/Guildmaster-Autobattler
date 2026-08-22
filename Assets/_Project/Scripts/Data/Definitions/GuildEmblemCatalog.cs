using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Набор знаков — тем помечают себя профиль и дом.
    /// </summary>
    /// <remarks>
    /// <b>Каталог, а не контент-тип на знак</b> (в отличие от <see cref="CursorSkinData"/>): у знака нет
    /// ни своей механики, ни лок-ключей, ни цены — это ровно картинка с именем. Заводить под каждый
    /// ScriptableObject значило бы получить двадцать ассетов, отличающихся одним полем.
    /// <para><b>Белый силуэт на прозрачном — условие.</b> Цвет знака выбирает игрок, и накладывается он
    /// тинтом: белая фигура становится цветной, а цветную покрасить было бы нечем. То же требование, что
    /// у скинов курсора, и по той же причине.</para>
    /// <para><b>Пустой каталог — законное состояние.</b> Экран создания просто не покажет выбор знака:
    /// имя слоту всё равно нужно, а знака может не быть.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Guild Emblem Catalog", fileName = "GuildEmblemCatalog")]
    public sealed class GuildEmblemCatalog : ScriptableObject
    {
        /// <summary>Один знак: строковый id и его белый силуэт.</summary>
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Строковый id знака (emblem.dragon-head). Едет в сейв и по сети — не переименовывать.")]
            public string Id;

            [Tooltip("Белый силуэт на прозрачном, 512x512. Красится тинтом в цвет, выбранный игроком.")]
            public Texture2D Image;
        }

        [SerializeField] private List<Entry> _emblems = new List<Entry>();

        /// <summary>Все знаки набора в порядке показа.</summary>
        public IReadOnlyList<Entry> Emblems => _emblems;

        /// <summary>
        /// Картинка знака по id. Неизвестный id — <c>null</c>, и это законный ответ: знак приходит из
        /// сейва и по сети, то есть снаружи, а показывать вместо него чужой знак хуже, чем ничего.
        /// </summary>
        public Texture2D Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < _emblems.Count; i++)
                if (_emblems[i].Id == id) return _emblems[i].Image;

            return null;
        }
    }
}
