using System;
using Guildmaster.Core.Flow;
using Guildmaster.Presentation.Effects;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Задний фон ЭКРАНОВ МЕТЫ: настройки, пауза, главное меню. Живёт на СВОЕЙ камере со своим слоем —
    /// поэтому ни с чем в мире не спорит и ничего собой не заслоняет.
    /// </summary>
    /// <remarks>
    /// Начинал жизнь как фон одного лишь главного меню, но с QA #50 это ЕДИНСТВЕННЫЙ задник в игре: под
    /// ивентом, магазином, сундуком и наградой раньше лежала своя непрозрачная заливка UI-цветом, и рядом
    /// с настоящим столом она читалась как чёрный экран. Теперь UI только говорит, нужен ли фон
    /// (<see cref="ScreenBackdropChangedEvent"/>), а как он выглядит — знает одно это место.
    /// Имя класса осталось прежним намеренно: его держит ссылка компонента в сцене.
    /// </remarks>
    /// <remarks>
    /// Почему отдельная камера, а не квад перед основной: меню показывают, стоя посреди живой арены, и
    /// подложить под него мировой объект не выходит — 2D-рендерер рисует спрайты после непрозрачной
    /// геометрии, так что тайлмап арены перекрывал стол на любом расстоянии, вплоть до вплотную к объективу.
    /// Своя камера с собственной маской слоёв снимает вопрос порядка целиком: она видит ровно один квад,
    /// перекрывает основную по depth, пока меню открыто, и гаснет вместе с ним.
    /// <para>Материал берётся из <see cref="MapStyle"/>, но СВОЙ — не тот, что под картой (05.08.2026).
    /// Прежде здесь лежал стол, и обоснованием было «фон меню и фон карты обязаны быть одной
    /// поверхностью». Оно перестало работать, когда мета получила собственный регистр: стол принадлежит
    /// гроссбуху — миру и забегу, — а настройки и меню стоят по другую сторону этой границы. Вход в
    /// забег и без общей поверхности не читается сменой сцены: между ними идёт шторка перехода.</para>
    /// </remarks>
    public sealed class MenuBackdropView : MonoBehaviour
    {
        [Tooltip("Единый стиль карты — отсюда берётся материал задника меты. Пусто = фона не будет.")]
        [SerializeField] private MapStyle _style;

        [Tooltip("Слой, который видит ТОЛЬКО камера меню. Мир на этом слое ничего не держит.")]
        [SerializeField] private string _layerName = "MenuBackdrop";

        [Tooltip("Порядок камеры меню. Должен быть выше боевой, чтобы она рисовалась поверх.")]
        [SerializeField] private float _cameraDepth = 100f;

        [Tooltip("Половина высоты кадра фона в мировых единицах. На крупность рисунка НЕ влияет: квад всегда " +
                 "покрывает кадр целиком, а масштаб рисунка задаётся тайлингом из MapStyle.")]
        [SerializeField] private float _viewHeight = 8f;

        private ISubscriber<ScreenBackdropChangedEvent> _menuSub;
        private ISubscriber<MenuBattleChangedEvent> _battleSub;
        private VisualToggles _toggles;
        private IDisposable _sub;
        private IDisposable _battleSubscription;

        private Camera _camera;
        private MeshRenderer _quad;
        private MaterialPropertyBlock _block;
        private bool _menuOpen;
        private bool _enabledByToggle = true;
        // За меню может идти живой бой (04.08.2026). Тогда стол не просто лишний — он закрывает собой
        // ровно то, ради чего бой и заведён.
        private bool _battleBehind;
        // ...но экран может попросить стол ЯВНО (настройки: кадр занят целиком, панели нет). Такой запрос
        // бой не отменяет — смотреть под настройки незачем, а мельтешение арены мешает читать строки.
        private bool _overBattle;

        private static readonly int AspectXId = Shader.PropertyToID("_AspectX");

        [Inject]
        public void Construct(ISubscriber<ScreenBackdropChangedEvent> menuSub,
                              ISubscriber<MenuBattleChangedEvent> battleSub,
                              VisualToggles toggles)
        {
            _menuSub   = menuSub;
            _battleSub = battleSub;
            _toggles   = toggles;
        }

        private void Start()
        {
            _sub = _menuSub?.Subscribe(e => SetOpen(e.Visible, e.OverBattle));
            _battleSubscription = _battleSub?.Subscribe(e => { _battleBehind = e.Running; ApplyVisibility(); });

            _toggles?.Register("menu.table", "Стол за экранами",
                on => { _enabledByToggle = on; ApplyVisibility(); });

            ApplyVisibility();
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _battleSubscription?.Dispose();
            _toggles?.Unregister("menu.table");
        }

        /// <summary>Просят ли задник прямо сейчас. Пара к <see cref="SetOpen"/>: без неё нельзя вернуть
        /// экран как было после временного показа.</summary>
        public bool IsOpen => _menuOpen;

        /// <summary>
        /// Показать или убрать задник меты. Обычно зовётся событием
        /// <see cref="ScreenBackdropChangedEvent"/> из UI.
        /// </summary>
        /// <remarks>
        /// <b>Открыт наружу для прогона кадров экранов</b> (<c>UiScreenSheet</c>): прогон собирает
        /// экраны мимо роутера, событие про задник публиковать некому — и снимок показывал интерфейс
        /// на чёрном поле вместо стола, который видит игрок (наход. Макса 23.08.2026). Второй способ
        /// поднять задник разошёлся бы с этим на первой же готче: видимость считается сразу из четырёх
        /// условий (<see cref="ApplyVisibility"/>), и повторять их снаружи нельзя.
        /// </remarks>
        /// <param name="overBattle">Просить стол даже поверх живого боя — так делают настройки.</param>
        public void SetOpen(bool open, bool overBattle)
        {
            _menuOpen = open;
            _overBattle = overBattle;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool show = _menuOpen && (!_battleBehind || _overBattle) && _enabledByToggle
                        && _style != null && _style.MenuBackdropMaterial != null;
            if (show && _camera == null) Build();
            if (_camera != null) _camera.gameObject.SetActive(show);
            if (show) Fit();
        }

        private void Build()
        {
            int layer = LayerMask.NameToLayer(_layerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[MenuBackdropView] - слой '{_layerName}' не заведён в проекте → фон меню " +
                                 "рисоваться не будет. Добавить слой в Project Settings / Tags and Layers.");
                layer = 0;
            }

            var camGo = new GameObject("MenuBackdropCamera") { layer = layer };
            camGo.transform.SetParent(transform, false);

            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = _viewHeight;
            _camera.cullingMask = 1 << layer;                 // видит ровно один квад и больше ничего
            _camera.clearFlags = CameraClearFlags.SolidColor;  // сама заливает кадр — мир под ней не просвечивает
            _camera.backgroundColor = Color.black;
            _camera.depth = _cameraDepth;                      // рисуется поверх боевой камеры
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "MenuBackdropQuad";
            quadGo.layer = layer;
            Destroy(quadGo.GetComponent<Collider>()); // фон не должен ловить клики по меню
            quadGo.transform.SetParent(camGo.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, 0f, 5f);

            _quad = quadGo.GetComponent<MeshRenderer>();
            _quad.sharedMaterial = _style.MenuBackdropMaterial;
            _quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quad.receiveShadows = false;
        }

        // Кадр может сменить пропорции (окно тянут) — подгоняем размер и пропорции каждый кадр, пока видны.
        private void LateUpdate()
        {
            if (_camera != null && _camera.gameObject.activeSelf) Fit();
        }

        private void Fit()
        {
            if (_camera == null || _quad == null) return;

            _camera.orthographicSize = _viewHeight;

            float height = _viewHeight * 2f;
            float width = height * _camera.aspect;
            _quad.transform.localScale = new Vector3(width, height, 1f);

            // Пропорция кадра — единственное, что шлём: остальное задник держит в материале. Без неё
            // картинка растянулась бы по широкой стороне, и диагональные лучи поехали бы углом.
            _block ??= new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_block);
            _block.SetFloat(AspectXId, height > 0.01f ? width / height : 1f);
            _quad.SetPropertyBlock(_block);
        }
    }
}
