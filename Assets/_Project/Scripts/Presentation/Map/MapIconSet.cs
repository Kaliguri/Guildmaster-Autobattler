using System;
using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Вид узла карты: иконка и её акцент. Домен про иконки не знает — сопоставление «тип узла → как выглядит»
    /// живёт здесь и правится в инспекторе без кода.
    /// </summary>
    [Serializable]
    public struct MapNodeIcon
    {
        [Tooltip("Ключ вида — совпадает с именем типа узла (Battle, Elite, Boss, Shop, Chest, TextEvent, Start).")]
        public string Kind;

        public Sprite Icon;

        [Tooltip("Цвет подложки под иконкой. Прозрачный = подложку не рисуем.")]
        public Color Backing;
    }

    /// <summary>
    /// Набор иконок узлов карты. Ключ — строковое имя типа узла: <c>Guildmaster.Presentation</c> не ссылается
    /// на <c>Guildmaster.Guild</c>, где живёт <c>MapNodeType</c>, и заводить эту связь ради картинок не стоит.
    /// Слой склейки (<c>Game</c>) передаёт имя типа, набор отдаёт вид.
    /// </summary>
    [CreateAssetMenu(fileName = "MapIconSet", menuName = "Guildmaster/Map/Icon Set")]
    public sealed class MapIconSet : ScriptableObject
    {
        [Tooltip("Сопоставление вид→иконка. Тип без записи рисуется запасным видом.")]
        [SerializeField] private MapNodeIcon[] _icons = Array.Empty<MapNodeIcon>();

        [Header("Запасной вид")]
        [Tooltip("Иконка для типа, которого нет в списке (или для «ещё не разведано»).")]
        [SerializeField] private Sprite _fallbackIcon;
        [SerializeField] private Color _fallbackBacking = new Color(0.55f, 0.55f, 0.55f);

        [Header("Размер")]
        [Tooltip("Высота иконки в мировых единицах. Спрайты Honeti идут с разным PPU, поэтому масштаб " +
                 "считаем от нужной высоты, а не доверяем импорту.")]
        [SerializeField] private float _worldHeight = 1.1f;

        /// <inheritdoc cref="_worldHeight"/>
        public float WorldHeight => _worldHeight;

        /// <summary>Вид узла по имени типа. Неизвестный тип → запасной вид (никогда не пусто).</summary>
        public MapNodeIcon Resolve(string kind)
        {
            if (_icons != null && !string.IsNullOrEmpty(kind))
            {
                for (int i = 0; i < _icons.Length; i++)
                    if (string.Equals(_icons[i].Kind, kind, StringComparison.OrdinalIgnoreCase))
                        return _icons[i];
            }
            return new MapNodeIcon { Kind = kind, Icon = _fallbackIcon, Backing = _fallbackBacking };
        }
    }
}
