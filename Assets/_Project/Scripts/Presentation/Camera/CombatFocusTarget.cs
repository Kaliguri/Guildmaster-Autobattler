using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Цель слежения для экшн-камеры (вики «16» §5): каждый кадр ставит свой transform в
    /// сглаженный центроид точек фокуса и считает «разброс» боя (для динамического зума).
    /// Точки берёт из <see cref="IFocusPointSource"/> (в бою — живые юниты, вне боя — пусто),
    /// сам ничего не мутирует. Экшн-камера Cinemachine следует за этим transform (поле Follow),
    /// а <see cref="CameraModeController"/> подгоняет орто-размер под <see cref="Spread"/>.
    /// </summary>
    public sealed class CombatFocusTarget : MonoBehaviour
    {
        [Tooltip("Скорость сглаживания позиции (больше — резче следует за центром боя).")]
        [SerializeField] private float _positionDamping = 4f;

        [Tooltip("Мёртвая зона слежения (мировые ед.): пока центр боя дрейфует в этом радиусе от камеры — " +
                 "камера СТОИТ (гасит тряску от толкотни юнитов), тянется только когда центр уехал за радиус. " +
                 "Масштаб поля 40×20, рост юнита 1.7 → ~3 = пара ростов, реально держит кадр.")]
        [SerializeField] private float _deadZoneRadius = 3f;

        [Tooltip("Скорость сглаживания разброса (для зума). Меньше — плавнее реакция зума на смерть/рывок юнита.")]
        [SerializeField] private float _spreadDamping = 2.5f;

        private IFocusPointSource _source;
        // На первом кадре с точками снапаем центр/разброс, чтобы камера не «наезжала» рывком со старта.
        private bool _initialized;

        /// <summary>Есть ли точки фокуса в кадре (иначе центроид/разброс не обновляются).</summary>
        public bool HasUnits { get; private set; }

        /// <summary>Полу-разброс боя: макс. расстояние точки фокуса от центроида (для подгона зума).</summary>
        public float Spread { get; private set; }

        // Источник инъектится боевым скоупом; при персист-камере переключается по контексту
        // (бой ↔ вне боя) через SetSource. До инъекции — пусто (камера стоит).
        [Inject]
        public void Construct(IFocusPointSource source) => _source = source;

        /// <summary>Сменить источник точек фокуса (вход/выход боя). null → камера ни за кем не следует.</summary>
        public void SetSource(IFocusPointSource source)
        {
            _source = source ?? EmptyFocusPointSource.Instance;
            _initialized = false; // следующий кадр снапнет центр/разброс на новый источник без рывка
        }

        private void LateUpdate()
        {
            if (_source == null) return;

            var points = _source.FocusPoints;
            Vector2 sum = Vector2.zero;
            int count = points.Count;
            for (int i = 0; i < count; i++)
                sum += points[i];

            HasUnits = count > 0;
            if (!HasUnits) return;

            Vector2 centroid = sum / count;

            float maxSqr = 0f;
            for (int i = 0; i < count; i++)
            {
                float d = (points[i] - centroid).sqrMagnitude;
                if (d > maxSqr) maxSqr = d;
            }
            float rawSpread = Mathf.Sqrt(maxSqr);

            Vector3 cur = transform.position;
            var centroid3 = new Vector3(centroid.x, centroid.y, cur.z);

            // Первый кадр с юнитами: снап (иначе камера «наезжает» рывком от стартовой позиции рига).
            if (!_initialized)
            {
                _initialized = true;
                Spread = rawSpread;
                transform.position = centroid3;
                return;
            }

            // Мёртвая зона: цель слежения = центроид, но пока он в радиусе _deadZoneRadius от камеры —
            // держим камеру на месте (тряска от толкотни юнитов сюда не проходит). Уехал за радиус —
            // целимся в КРАЙ зоны (центроид, «подтянутый» назад на радиус), поэтому камера догоняет мягко,
            // без рывка на всю величину. Тот же двухрадиусный гистерезис, что и у подхода юнитов (§движение).
            Vector2 delta = centroid - (Vector2)cur;
            float drift = delta.magnitude;
            Vector3 followTarget = drift <= _deadZoneRadius
                ? cur
                : (Vector3)(centroid - delta / drift * _deadZoneRadius) + Vector3.forward * cur.z;

            // Экспоненциальное сглаживание, независимое от частоты кадров. Разброс сглаживаем отдельно:
            // сырой скачет при смерти/рывке (телепорт монаха, отбрасывание) и дёргал бы зум.
            float tPos    = 1f - Mathf.Exp(-_positionDamping * Time.deltaTime);
            float tSpread = 1f - Mathf.Exp(-_spreadDamping   * Time.deltaTime);
            Spread = Mathf.Lerp(Spread, rawSpread, tSpread);
            transform.position = Vector3.Lerp(cur, followTarget, tPos);
        }
    }
}
