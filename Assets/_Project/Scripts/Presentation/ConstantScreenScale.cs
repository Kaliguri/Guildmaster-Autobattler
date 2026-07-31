using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Держит постоянный ЭКРАННЫЙ размер world-space объекта (надголовный HP/мана-бар) независимо от зума
    /// боевой ортокамеры. У ортокамеры видимый размер ∝ 1/orthographicSize, поэтому каждый кадр домножаем
    /// авторский <c>localScale</c> на <c>orthographicSize / reference</c>: экранный размер перестаёт зависеть
    /// от зума, а связь с масштабом юнита и родителя сохраняется (множитель мультипликативен поверх них).
    /// <para>Ставится на КОНТЕЙНЕР надголовного UI (общий родитель баров), а НЕ на сами бары: их
    /// <c>localScale</c> занят punch'ем/анимацией, и второй писатель дрался бы за него.</para>
    /// <para>Камера — <see cref="Camera.main"/> (выход Cinemachine-Brain, живой орто-размер бленда). Не
    /// ортокамера или камеры нет — множитель 1, компенсации нет.</para>
    /// </summary>
    public sealed class ConstantScreenScale : MonoBehaviour
    {
        [Tooltip("Опорный орто-размер камеры: при нём объект виден в свой АВТОРСКИЙ размер (множитель = 1). " +
                 "Меньше опорный — калибровка под более близкий зум, объект на экране КРУПНЕЕ при любом зуме.")]
        [SerializeField] private float _referenceOrthographicSize = 5f;

        private Vector3 _baseScale = Vector3.one;
        private bool    _captured;
        private Camera  _cam;

        // Авторский масштаб снимаем ОДИН раз, до первой компенсации: иначе на втором включении базой стал бы
        // уже домноженный масштаб и объект «поехал» бы с каждым циклом enable.
        private void OnEnable()
        {
            if (_captured) return;
            _baseScale = transform.localScale;
            _captured  = true;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            transform.localScale = _baseScale * ZoomFactor(_cam, _referenceOrthographicSize);
        }

        /// <summary>
        /// Множитель, удерживающий экранный размер постоянным при зуме ортокамеры: <c>orthoSize / reference</c>.
        /// Общий для всех потребителей (бары, боевые цифры) — формула живёт здесь в одном месте.
        /// Не ортокамера / нет камеры / некорректный reference → 1 (без компенсации).
        /// </summary>
        public static float ZoomFactor(Camera cam, float referenceOrthographicSize)
        {
            if (cam == null || !cam.orthographic || referenceOrthographicSize <= 0f) return 1f;
            return cam.orthographicSize / referenceOrthographicSize;
        }
    }
}
