using System;
using System.Collections.Generic;
using Shapes;
using UnityEngine;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Узел карты как ОДИН префаб на все типы: внутри лежат все варианты иконок выключенными, код включает
    /// нужный. Отдельный префаб на тип не нужен — типы больше не различаются цветом или формой, только
    /// картинкой, и держать восемь почти одинаковых префабов значило бы править каждую настройку восемь раз.
    /// </summary>
    public sealed class MapNodeView : MonoBehaviour
    {
        /// <summary>Иконка одного типа узла внутри общего префаба.</summary>
        /// <remarks>
        /// Здесь ГРУППА, а не один спрайт: элитный бой — это два меча, и таких составных значков будет
        /// больше. Ссылка на объект-корень позволяет собрать иконку из скольких угодно частей и не плодить
        /// поле на каждую; красятся все спрайты внутри разом.
        /// </remarks>
        [Serializable]
        public struct IconVariant
        {
            [Tooltip("Имя типа узла (Battle, Elite, Boss, Shop, Chest, Camp, TextEvent, Start, Unknown).")]
            public string Kind;

            [Tooltip("Корень иконки этого типа: сам спрайт ИЛИ объект-группа с несколькими спрайтами внутри " +
                     "(элита = два меча). Держится выключенным, включается по типу узла.")]
            public GameObject Icon;
        }

        [Header("Части узла")]
        [Tooltip("Что масштабируется откликом (hover/нажатие). Пусто — масштабируется весь узел.")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("Подложка-фигура (Shapes). Цвет берётся из палитры карты — один тон на все типы.")]
        [SerializeField] private ShapeRenderer _backingShape;

        [Tooltip("Подложка-спрайт. Альтернатива фигуре, если под узел нарисуют картинку.")]
        [SerializeField] private SpriteRenderer _backingSprite;

        [Tooltip("Обод узла. Необязателен.")]
        [SerializeField] private ShapeRenderer _rim;

        [Tooltip("Метка узла, на котором стоит отряд. Включается только для текущего узла.")]
        [SerializeField] private GameObject _currentMarker;

        [Header("Иконки типов")]
        [Tooltip("Все варианты иконок. Активен ровно один — по типу узла.")]
        [SerializeField] private IconVariant[] _variants = Array.Empty<IconVariant>();

        [Header("Поведение")]
        [Tooltip("Радиус попадания мышью (мировые единицы). Обычно чуть больше видимого круга — " +
                 "попасть по узлу должно быть легче, чем он выглядит.")]
        [SerializeField] private float _pickRadius = 0.6f;

        [Tooltip("ВИДИМЫЙ радиус узла (внешний край круга с ободом). От него отступает дорожка, чтобы " +
                 "точки не вползали под узел. Не путать с радиусом хвата — тот больше намеренно.")]
        [SerializeField] private float _visualRadius = 0.6f;

        // Спрайты активной иконки: у составного значка (элита — два меча) их несколько, и красить надо все.
        private readonly List<SpriteRenderer> _activeIcon = new List<SpriteRenderer>(4);
        private MaterialPropertyBlock _mpb;

        /// <inheritdoc cref="_pickRadius"/>
        public float PickRadius => _pickRadius;

        /// <inheritdoc cref="_visualRadius"/>
        public float VisualRadius => _visualRadius;

        /// <summary>Включает иконку нужного типа, гасит остальные. Неизвестный тип — без иконки.</summary>
        public void ShowKind(string kind)
        {
            _activeIcon.Clear();
            for (int i = 0; i < _variants.Length; i++)
            {
                GameObject icon = _variants[i].Icon;

                // Оборванная ссылка (переименовали объект в префабе, сменили тип поля) роняла ВЕСЬ забег
                // прямо в отрисовке карты. Узел без картинки — заметный, но переживаемый дефект, поэтому
                // здесь предупреждение, а не исключение; молча пропускать тоже нельзя.
                if (icon == null)
                {
                    if (!string.IsNullOrEmpty(_variants[i].Kind))
                        Debug.LogWarning($"[MapNodeView] - у типа '{_variants[i].Kind}' не назначена иконка " +
                                         $"(префаб {name}) → узел будет без картинки.", this);
                    continue;
                }

                bool match = string.Equals(_variants[i].Kind, kind, StringComparison.OrdinalIgnoreCase);
                icon.SetActive(match);
                if (match) icon.GetComponentsInChildren(includeInactive: true, _activeIcon);
            }
        }

        /// <summary>Тон карты и состояние узла. Палитра обязательна — цвета живут только в ней.</summary>
        public void Apply(MapNodeVisualState state, MapStyle palette)
        {
            _state   = state;
            _palette = palette;
            ApplyColors(1f);
        }

        /// <summary>
        /// Дыхание яркости для доступных узлов. Отдельно от <see cref="Apply"/>, потому что зовётся
        /// каждый кадр: состояние при этом не меняется, меняется только сила свечения.
        /// </summary>
        public void SetBrightness(float brightness) => ApplyColors(brightness);

        private MapNodeVisualState _state;
        private MapStyle _palette;

        private void ApplyColors(float brightness)
        {
            MapStyle palette = _palette;
            if (palette == null) return;

            MapNodeVisualState state = _state;
            Color tint = palette.StateTint(state) * brightness;
            tint.a = 1f; // подложка непрозрачна в любом состоянии — состояние читается яркостью

            SetColor(_backingShape, _backingSprite, Mul(palette.NodeBacking, tint));
            if (_rim != null) _rim.Color = Mul(palette.NodeRim, tint);

            for (int i = 0; i < _activeIcon.Count; i++)
            {
                SpriteRenderer icon = _activeIcon[i];
                if (icon == null) continue;

                // Состояние — через SpriteRenderer.color (vertex color в шейдере), рампа — через MPB.
                // MPB, а не sharedMaterial: правка материала в рантайме пачкает ассет на диске.
                icon.color = tint;
                _mpb ??= new MaterialPropertyBlock();
                icon.GetPropertyBlock(_mpb);
                _mpb.SetColor(ShadowId, palette.IconShadow);
                _mpb.SetColor(LightId, palette.IconLight);
                icon.SetPropertyBlock(_mpb);
            }

            if (_currentMarker != null)
            {
                _currentMarker.SetActive(state == MapNodeVisualState.Current);
                var marker = _currentMarker.GetComponent<ShapeRenderer>();
                if (marker != null) marker.Color = palette.CurrentMarker;
            }
        }

        /// <summary>Отклик: hover/нажатие. Трогает только визуальный корень — зона хвата не «дышит».</summary>
        public void SetVisualScale(float scale)
        {
            Transform t = _visualRoot != null ? _visualRoot : transform;
            t.localScale = Vector3.one * scale;
        }

        private static readonly int ShadowId = Shader.PropertyToID("_ShadowColor");
        private static readonly int LightId  = Shader.PropertyToID("_LightColor");

        private static void SetColor(ShapeRenderer shape, SpriteRenderer sprite, Color c)
        {
            if (shape != null) shape.Color = c;
            else if (sprite != null) sprite.color = c;
        }

        private static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
    }
}
