using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Y-сортировка спрайта: каждый кадр ставит <see cref="SpriteRenderer.sortingOrder"/> по мировой Y —
    /// кто ниже (ближе к зрителю), тот рисуется поверх. Явный ордер стабильнее transparency-sort по позиции:
    /// у перекрытых спрайтов с одинаковым ордером иначе дрожит порядок отрисовки (мерцание).
    /// <para>Юниты сортируются кодом в <see cref="UnitView"/> (без навешивания компонента). Этот компонент —
    /// для остального: пропов, декора, ручных объектов на одном слое сортировки.</para>
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class YSortSprite : MonoBehaviour
    {
        [Tooltip("Спрайт для сортировки. Пусто = взять с этого GameObject.")]
        [SerializeField] private SpriteRenderer _renderer;
        [Tooltip("Ордеров на 1 мировую единицу Y. Больше = тоньше (детальнее) сортировка.")]
        [SerializeField] private int _precision = 100;
        [Tooltip("Смещение точки сортировки по Y (напр. к ногам объекта, если пивот в центре).")]
        [SerializeField] private float _yPivot = 0f;

        private void Reset()  => _renderer = GetComponent<SpriteRenderer>();
        private void Awake()  { if (_renderer == null) _renderer = GetComponent<SpriteRenderer>(); }

        private void LateUpdate()
        {
            if (_renderer == null) return;
            _renderer.sortingOrder = -Mathf.RoundToInt((transform.position.y + _yPivot) * _precision);
        }
    }
}
