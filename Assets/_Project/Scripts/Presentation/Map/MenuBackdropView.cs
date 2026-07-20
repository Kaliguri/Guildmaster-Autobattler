using System;
using Guildmaster.Core.Flow;
using Guildmaster.Presentation.Effects;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Map
{
    /// <summary>
    /// Фон главного меню: тот же стол, что под картой акта. Живёт на СВОЕЙ камере со своим слоем —
    /// поэтому ни с чем в мире не спорит и ничего собой не заслоняет.
    /// </summary>
    /// <remarks>
    /// Почему отдельная камера, а не квад перед основной: меню показывают, стоя посреди живой арены, и
    /// подложить под него мировой объект не выходит — 2D-рендерер рисует спрайты после непрозрачной
    /// геометрии, так что тайлмап арены перекрывал стол на любом расстоянии, вплоть до вплотную к объективу.
    /// Своя камера с собственной маской слоёв снимает вопрос порядка целиком: она видит ровно один квад,
    /// перекрывает основную по depth, пока меню открыто, и гаснет вместе с ним.
    /// <para>Материал берётся из <see cref="MapStyle"/> — тот же, что под картой. Это не экономия: фон меню
    /// и фон карты обязаны быть одной поверхностью, иначе вход в забег читается как смена сцены.</para>
    /// </remarks>
    public sealed class MenuBackdropView : MonoBehaviour
    {
        [Tooltip("Единый стиль карты — отсюда берётся материал стола. Пусто = фона не будет.")]
        [SerializeField] private MapStyle _style;

        [Tooltip("Слой, который видит ТОЛЬКО камера меню. Мир на этом слое ничего не держит.")]
        [SerializeField] private string _layerName = "MenuBackdrop";

        [Tooltip("Порядок камеры меню. Должен быть выше боевой, чтобы она рисовалась поверх.")]
        [SerializeField] private float _cameraDepth = 100f;

        [Tooltip("Половина высоты кадра фона в мировых единицах. На крупность рисунка НЕ влияет: квад всегда " +
                 "покрывает кадр целиком, а масштаб рисунка задаётся тайлингом из MapStyle.")]
        [SerializeField] private float _viewHeight = 8f;

        private ISubscriber<MainMenuVisibilityChangedEvent> _menuSub;
        private VisualToggles _toggles;
        private IDisposable _sub;

        private Camera _camera;
        private MeshRenderer _quad;
        private MaterialPropertyBlock _block;
        private bool _menuOpen;
        private bool _enabledByToggle = true;

        private static readonly int AspectXId = Shader.PropertyToID("_AspectX");
        private static readonly int LightStrengthId = Shader.PropertyToID("_LightStrength");
        private static readonly int AmbientId = Shader.PropertyToID("_Ambient");
        private static readonly int PatternTilingId = Shader.PropertyToID("_PatternTiling");

        [Inject]
        public void Construct(ISubscriber<MainMenuVisibilityChangedEvent> menuSub, VisualToggles toggles)
        {
            _menuSub = menuSub;
            _toggles = toggles;
        }

        private void Start()
        {
            _sub = _menuSub?.Subscribe(e => SetOpen(e.Visible));

            _toggles?.Register("menu.table", "Стол за главным меню",
                on => { _enabledByToggle = on; ApplyVisibility(); });

            ApplyVisibility();
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _toggles?.Unregister("menu.table");
        }

        private void SetOpen(bool open)
        {
            _menuOpen = open;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool show = _menuOpen && _enabledByToggle && _style != null && _style.TableMaterial != null;
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
            _quad.sharedMaterial = _style.TableMaterial;
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

            // Пропорции — в шейдер, иначе тайл стола растянется в полосы вдоль широкой стороны экрана.
            _block ??= new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_block);
            // Подача стола в меню — из MapStyle, рядом с карточной: один ассет держит оба набора чисел,
            // и разницу между «под картой» и «в меню» видно, не открывая два места.
            _block.SetFloat(AspectXId, height > 0.01f ? width / height : 1f);
            _block.SetFloat(LightStrengthId, _style.MenuTableLight);
            _block.SetFloat(AmbientId, _style.MenuTableAmbient);
            _block.SetFloat(PatternTilingId, _style.MenuTableTiling);
            _quad.SetPropertyBlock(_block);
        }
    }
}
