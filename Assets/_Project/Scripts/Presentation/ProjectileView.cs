using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// World-space вид снаряда (Bullet). Следует за СНИМКАМИ снаряда из ленты боя, интерполируя между
    /// тиками показа (как <see cref="UnitView"/>), повёрнутый по вектору скорости.
    /// Тинтуется цветом-эффектом источника. Ссылки на живой снаряд не держит намеренно: сим ушёл вперёд
    /// на окно опережения, и по живому снаряду вид вылетал бы до выстрела.
    /// <para>Синхрон импакта: снаряд исчезает из кадра в том же тике, в котором показан урон, — значит
    /// вид гаснет ровно там, где появляется цифра.</para>
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _sprite;

        [Tooltip("След за снарядом. Пусто — снаряд летит без следа.")]
        [SerializeField] private TrailRenderer _trail;

        [Tooltip("Яркость следа относительно тинта снаряда: след — свечение позади, а не второй снаряд.")]
        [SerializeField, Range(0.2f, 2f)] private float _trailBrightness = 1.15f;

        [Header("Растяжение по скорости")]
        [Tooltip("Насколько снаряд вытягивается вдоль полёта за каждую единицу скорости. 0 = не тянется.")]
        [SerializeField, Range(0f, 0.2f)] private float _stretchPerSpeed = 0.03f;
        [Tooltip("Потолок растяжения (во сколько раз длиннее исходного). 1 = растяжения нет.")]
        [SerializeField, Range(1f, 3f)] private float _maxStretch = 1.7f;

        // Как быстро визуальный офсет «старта из дула» сходит на симовую траекторию (1/сек). Больше = резче.
        private const float OriginConvergeRate = 12f;

        private Vector3    _originOffset; // визуальный старт из ShotPoint: разница (дуло − центр сима), затухает к 0
        private Vector3    _baseScale = Vector3.one;   // масштаб префаба; снимается один раз (вид приходит из пула)
        private bool       _baseScaleCaptured;

        /// <summary>
        /// Привязать к снимку снаряда: вид следует за лентой, а не за живым снарядом. По живому он
        /// вылетал бы за окно опережения до выстрела и прилетал задолго до цифры урона.
        /// </summary>
        public void BindVisual(Color tint, Vector3 visualOrigin, Vector2 simPosition, Vector2 velocity)
        {
            if (_sprite != null) _sprite.color = tint;
            Vector3 simPos = new Vector3(simPosition.x, simPosition.y, 0f);
            _originOffset = visualOrigin - simPos;
            transform.position = simPos + _originOffset;
            FaceVelocity(velocity);
            ApplyTrail(tint);
        }

        /// <summary>
        /// Поставить позицию по снимкам показанного тика: <paramref name="previous"/> → <paramref name="current"/>
        /// с долей <paramref name="alpha"/>. Смещение к дулу сходит на нет по мере полёта.
        /// </summary>
        public void Follow(Vector2 current, Vector2 previous, Vector2 velocity, float alpha)
        {
            Vector3 simPos = Vector3.Lerp(
                new Vector3(previous.x, previous.y, 0f), new Vector3(current.x, current.y, 0f), alpha);

            // Офсет дула затухает к нулю — снаряд сходит на симовую траекторию задолго до импакта.
            _originOffset = Vector3.Lerp(
                _originOffset, Vector3.zero, 1f - Mathf.Exp(-OriginConvergeRate * Time.deltaTime));
            transform.position = simPos + _originOffset;
            FaceVelocity(velocity);
        }

        /// <summary>
        /// След — ГЛАВНЫЙ цвет источника, теряющий прозрачность к хвосту. Именно так, а не переходом в
        /// другой цвет: шлейф — это тот же свет, что и снаряд, просто затухающий. Ярче тела, чтобы хвост
        /// читался как свечение позади, а не как второй снаряд.
        /// <para>Clear ОБЯЗАТЕЛЕН: вид приходит из пула, и без сброса точек хвост тянется через весь экран
        /// от места, где кончился прошлый выстрел.</para>
        /// </summary>
        private void ApplyTrail(Color tint)
        {
            if (_trail == null) return;

            _trail.Clear();

            Color head = tint * _trailBrightness;
            head.a = 1f;
            Color tail = head;
            tail.a = 0f;

            _trail.startColor = head;
            _trail.endColor   = tail;
            _trail.emitting   = true;
        }

        /// <summary>Погасить след перед возвратом в пул — точки хвоста не должны пережить снаряд.</summary>
        private void OnDisable()
        {
            if (_trail == null) return;
            _trail.emitting = false;
            _trail.Clear();
        }

        // Спрайт нарисован «вправо»: поворачиваем по направлению полёта (в сторону цели) и вытягиваем вдоль него.
        private void FaceVelocity(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            ApplyStretch(v.magnitude);
        }

        /// <summary>
        /// Вытягивание вдоль полёта: быстрый снаряд читается как быстрый, а не как медленный кружок.
        /// Объём сохраняем (поперёк сжимаем на корень) — иначе быстрый выстрел просто раздувается.
        /// </summary>
        private void ApplyStretch(float speed)
        {
            if (_stretchPerSpeed <= 0f || _maxStretch <= 1f) return;

            if (!_baseScaleCaptured) { _baseScale = transform.localScale; _baseScaleCaptured = true; }

            float stretch = Mathf.Clamp(1f + speed * _stretchPerSpeed, 1f, _maxStretch);
            float squash  = 1f / Mathf.Sqrt(stretch);
            transform.localScale = new Vector3(_baseScale.x * stretch, _baseScale.y * squash, _baseScale.z);
        }
    }
}
