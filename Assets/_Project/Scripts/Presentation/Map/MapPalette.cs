using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Цвета карты акта — единый источник. Дефолты взяты из токенов темы UI
    /// (<c>UI/Theme/tokens.primitives.uss</c>: семейства <c>--gm-ink</c>, <c>--gm-brass</c>,
    /// <c>--gm-parchment</c>), чтобы карта и интерфейс были одной эстетики «гильдейского гроссбуха»,
    /// а не двух похожих, но разъезжающихся палитр.
    /// </summary>
    /// <remarks>
    /// Тип узла цветом НЕ различается (решение Макса): подложка у всех одна, тип читается иконкой.
    /// Цвет на карте значит СОСТОЯНИЕ — где был, где стою, куда можно.
    /// </remarks>
    [CreateAssetMenu(fileName = "MapPalette", menuName = "Guildmaster/Map/Palette")]
    public sealed class MapPalette : ScriptableObject
    {
        [Header("Узел")]
        [Tooltip("Подложка узла — одна на все типы (--gm-ink-600).")]
        [SerializeField] private Color _nodeBacking = new Color(0.141f, 0.102f, 0.071f);

        [Tooltip("Обод узла (--gm-brass-600).")]
        [SerializeField] private Color _nodeRim = new Color(0.627f, 0.435f, 0.188f);

        [Tooltip("Метка узла, на котором стоит отряд (--gm-brass-200).")]
        [SerializeField] private Color _currentMarker = new Color(0.902f, 0.788f, 0.561f);

        [Header("Рампа иконок")]
        [Tooltip("Тёмный конец рампы перекраски иконки (--gm-ink-700).")]
        [SerializeField] private Color _iconShadow = new Color(0.110f, 0.078f, 0.051f);

        [Tooltip("Светлый конец рампы (--gm-parchment-100).")]
        [SerializeField] private Color _iconLight = new Color(0.937f, 0.886f, 0.769f);

        [Header("Состояния (множители яркости)")]
        [Tooltip("Куда можно шагнуть — полный цвет.")]
        [SerializeField] private Color _available = Color.white;

        [Tooltip("Где стоит отряд — полный цвет.")]
        [SerializeField] private Color _current = Color.white;

        [Tooltip("Пройденный узел — притушен, но не выключен: маршрут должен читаться.")]
        [SerializeField] private Color _cleared = new Color(0.62f, 0.58f, 0.5f);

        [Tooltip("Ещё не открытый узел — самый тусклый.")]
        [SerializeField] private Color _locked = new Color(0.42f, 0.4f, 0.36f);

        [Header("Пути")]
        [Tooltip("Обычный путь — бледные точки (--gm-parchment-300, приглушённый).")]
        [SerializeField] private Color _pathIdle = new Color(0.42f, 0.37f, 0.28f);

        [Tooltip("Пройденный маршрут — прочерченный чернилами по карте (--gm-brass-500).")]
        [SerializeField] private Color _pathTravelled = new Color(0.722f, 0.525f, 0.231f);

        [Tooltip("Путь к доступному узлу — самый яркий (--gm-brass-200).")]
        [SerializeField] private Color _pathAvailable = new Color(0.902f, 0.788f, 0.561f);

        [Header("Фишка")]
        [Tooltip("Точка отряда — того же семейства, что точки пути, но ярче.")]
        [SerializeField] private Color _pawn = new Color(0.973f, 0.925f, 0.796f);

        /// <inheritdoc cref="_nodeBacking"/>
        public Color NodeBacking => _nodeBacking;
        /// <inheritdoc cref="_nodeRim"/>
        public Color NodeRim => _nodeRim;
        /// <inheritdoc cref="_currentMarker"/>
        public Color CurrentMarker => _currentMarker;
        /// <inheritdoc cref="_iconShadow"/>
        public Color IconShadow => _iconShadow;
        /// <inheritdoc cref="_iconLight"/>
        public Color IconLight => _iconLight;
        /// <inheritdoc cref="_pathIdle"/>
        public Color PathIdle => _pathIdle;
        /// <inheritdoc cref="_pathTravelled"/>
        public Color PathTravelled => _pathTravelled;
        /// <inheritdoc cref="_pathAvailable"/>
        public Color PathAvailable => _pathAvailable;
        /// <inheritdoc cref="_pawn"/>
        public Color Pawn => _pawn;

        /// <summary>Множитель яркости по состоянию узла.</summary>
        public Color StateTint(MapNodeVisualState state) => state switch
        {
            MapNodeVisualState.Available => _available,
            MapNodeVisualState.Current   => _current,
            MapNodeVisualState.Cleared   => _cleared,
            _                            => _locked,
        };
    }
}
