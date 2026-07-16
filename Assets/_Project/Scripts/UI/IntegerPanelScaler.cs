using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Держит UI Toolkit-панель на ЦЕЛОМ множителе масштаба — ключ к хрусткому пиксель-арту.
    /// Дробный масштаб даёт билинейное мыло; целое кратное (референс 360p → 720p x2, 1080p x3,
    /// 1440p x4) выбирает пиксели один-в-один. Ставит <see cref="PanelSettings.scaleMode"/> в
    /// ConstantPixelSize и гонит <see cref="PanelSettings.scale"/> = floor(высота экрана / референс).
    /// <para>Вешается на любой объект UI-сцены; ссылку на общий <see cref="PanelSettings"/> назначить
    /// в инспекторе. Пересчёт — при смене высоты экрана (окно/разрешение), без аллокаций.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntegerPanelScaler : MonoBehaviour
    {
        [Tooltip("Общий PanelSettings, масштаб которого держим целым.")]
        [SerializeField] private PanelSettings _panelSettings;

        [Tooltip("Референсная высота UI в пикселях (база авторинга). 360 кратно ложится в 720/1080/1440.")]
        [SerializeField] private int _referenceHeight = 360;

        private int _lastHeight = -1;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (CurrentHeight() != _lastHeight) Apply();
        }

        private void Apply()
        {
            if (_panelSettings == null || _referenceHeight <= 0) return;

            _lastHeight = CurrentHeight();
            int scale = Mathf.Max(1, Mathf.FloorToInt((float)_lastHeight / _referenceHeight));

            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.scale = scale;
        }

        /// <summary>
        /// Высота вьюпорта для расчёта масштаба. В билде — <see cref="Screen.height"/>. В Editor
        /// <c>Screen.height</c> НЕНАДЁЖЕН: возвращает размер активного вида (Game ИЛИ Scene), зависит от
        /// фокуса окна, докинга и первых кадров play — из-за чего целочисленный scale «плавает» (UI то
        /// гигантский, то нормальный), а мутация записывается в общий PanelSettings-ассет и залипает.
        /// Поэтому в Editor берём фактический размер Game view.
        /// </summary>
        private int CurrentHeight()
        {
#if UNITY_EDITOR
            Vector2 gameView = UnityEditor.Handles.GetMainGameViewSize();
            if (gameView.y >= 1f) return Mathf.RoundToInt(gameView.y);
#endif
            return Screen.height;
        }
    }
}
