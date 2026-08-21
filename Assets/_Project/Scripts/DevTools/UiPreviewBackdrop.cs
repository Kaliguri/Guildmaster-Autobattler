#if UNITY_EDITOR
using Guildmaster.Presentation.Map;
using UnityEngine;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Задник меты на стенде UI: тот же материал, что в игре, без сеанса и подписок.
    /// </summary>
    /// <remarks>
    /// <b>Заведён потому, что стенд врал чернотой</b> (правка Макса 21.08.2026). В игре задник за
    /// экранами меты рисует <see cref="MenuBackdropView"/> по событию из UI, а стенд презентационный
    /// слой не поднимает вовсе — экраны без своей панели (настройки, развилки) выглядели на кадрах
    /// лежащими на чёрном. Кадр для приёмки, показывающий не то, что увидит игрок, хуже отсутствия
    /// кадра: по нему принимаются решения о цвете.
    ///
    /// <para><b>Ни событий, ни условий здесь нет.</b> В игре задник появляется и гаснет по тому, что
    /// на экране и идёт ли за ним бой; на стенде экран показан всегда и мира за ним не существует —
    /// значит и спрашивать нечего. Копировать сюда правила видимости значило бы завести им второго
    /// владельца ради стенда.</para>
    ///
    /// <para><b>Материал берётся из того же <see cref="MapStyle"/>,</b> что и в игре: свой был бы
    /// вторым источником правды о том, как выглядит фон меты, и разошёлся бы с первым в тот день,
    /// когда фон поменяют.</para>
    /// </remarks>
    [ExecuteAlways]
    public sealed class UiPreviewBackdrop : MonoBehaviour
    {
        [Tooltip("Стиль карты — отсюда берётся материал задника меты. Пусто = ищется в проекте по имени.")]
        [SerializeField] private MapStyle _style;

        [Tooltip("Половина высоты кадра в мировых единицах. То же число, что у задника в игре.")]
        [SerializeField] private float _viewHeight = 8f;

        private static readonly int AspectXId = Shader.PropertyToID("_AspectX");

        private Camera _camera;
        private MeshRenderer _quad;
        private MaterialPropertyBlock _block;

        private void OnEnable()
        {
            if (_style == null) _style = LoadStyle();
            if (_style == null || _style.MenuBackdropMaterial == null)
            {
                Debug.LogWarning("[UiPreviewBackdrop] - материала задника нет: стенд покажет чёрный фон, " +
                                 "как было до 21.08.2026. Проверь MapStyle.asset.");
                return;
            }

            Build();
            Fit();
        }

        private void OnDisable()
        {
            if (_camera == null) return;

            if (Application.isPlaying) Destroy(_camera.gameObject);
            else                       DestroyImmediate(_camera.gameObject);
            _camera = null;
            _quad = null;
        }

        private void LateUpdate()
        {
            if (_camera != null) Fit();
        }

        private static MapStyle LoadStyle()
        {
            string[] found = UnityEditor.AssetDatabase.FindAssets("t:MapStyle");
            if (found.Length == 0) return null;

            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<MapStyle>(path);
        }

        /// <summary>
        /// Своя камера с единственным квадом — тем же приёмом, что и в игре.
        /// </summary>
        /// <remarks>
        /// Слоя <c>MenuBackdrop</c> здесь не требуется: на стенде мира нет, спорить за порядок отрисовки
        /// не с кем, и камера просто стоит ниже той, что рисует интерфейс.
        /// </remarks>
        private void Build()
        {
            if (_camera != null) return;

            var camGo = new GameObject("UiPreviewBackdropCamera") { hideFlags = HideFlags.DontSave };
            camGo.transform.SetParent(transform, false);

            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = _viewHeight;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.depth = -10f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "UiPreviewBackdropQuad";
            quadGo.hideFlags = HideFlags.DontSave;
            Collider collider = quadGo.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else                       DestroyImmediate(collider);
            }

            quadGo.transform.SetParent(camGo.transform, false);
            quadGo.transform.localPosition = new Vector3(0f, 0f, 5f);

            _quad = quadGo.GetComponent<MeshRenderer>();
            _quad.sharedMaterial = _style.MenuBackdropMaterial;
            _quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quad.receiveShadows = false;
        }

        private void Fit()
        {
            if (_camera == null || _quad == null) return;

            float height = _viewHeight * 2f;
            float width = height * _camera.aspect;
            _quad.transform.localScale = new Vector3(width, height, 1f);

            // Пропорцию кадра задник ждёт извне — без неё диагональные лучи поедут углом.
            _block ??= new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_block);
            _block.SetFloat(AspectXId, height > 0.01f ? width / height : 1f);
            _quad.SetPropertyBlock(_block);
        }
    }
}
#endif
