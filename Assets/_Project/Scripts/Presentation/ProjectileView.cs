using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// World-space вид снаряда (Bullet). Держит ссылку на симовый <see cref="Projectile"/> и следует за ним
    /// интерполяцией между тиками (как <see cref="UnitView"/>), повёрнутый по вектору скорости (в сторону цели).
    /// Тинтуется цветом юнита-источника (тем же, что тело). Только читает симуляцию, не мутирует.
    /// <para>Синхрон импакта: при попадании сим ставит снаряду <c>Position</c> = точка удара и <c>IsAlive=false</c>
    /// в тот же тик, что и урон/цифру. Вид тогда снапается ровно в точку удара и гаснет — визуальный импакт
    /// совпадает с реальным эффектом, без рассинхрона.</para>
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

        private Projectile _projectile;
        private Vector3    _originOffset; // визуальный старт из ShotPoint: разница (дуло − центр сима), затухает к 0
        private Vector3    _baseScale = Vector3.one;   // масштаб префаба; снимается один раз (вид приходит из пула)
        private bool       _baseScaleCaptured;

        /// <summary>Привязать к симовому снаряду и затинтовать под источник (старт из центра сима).</summary>
        public void Bind(Projectile projectile, Color tint)
            => Bind(projectile, tint, new Vector3(projectile.Position.x, projectile.Position.y, 0f));

        /// <summary>
        /// Привязать со стартом из мировой точки <paramref name="visualOrigin"/> (дуло/ShotPoint источника):
        /// снаряд визуально вылетает оттуда и плавно сходит на симовую траекторию — сим стреляет из центра юнита.
        /// </summary>
        public void Bind(Projectile projectile, Color tint, Vector3 visualOrigin)
        {
            _projectile = projectile;
            if (_sprite != null) _sprite.color = tint;
            Vector3 simPos = new Vector3(projectile.Position.x, projectile.Position.y, 0f);
            _originOffset = visualOrigin - simPos;
            transform.position = simPos + _originOffset;
            FaceVelocity(projectile.Velocity);

            ApplyTrail(tint);
        }

        /// <summary>
        /// Обновить интерполированную позицию/поворот. Возвращает false, когда снаряд исчез (попал/вышел за поле):
        /// вид снапается в финальную точку (= точка удара) — вызывающий уничтожает вид.
        /// </summary>
        public bool Tick(float alpha)
        {
            if (_projectile == null) return false;

            if (!_projectile.IsAlive)
            {
                // Снап в точку удара (совпадает с кадром появления цифры урона) — импакт без рассинхрона.
                // Офсет дула тут НЕ применяем: точка удара должна совпасть с симом и цифрой урона.
                transform.position = new Vector3(_projectile.Position.x, _projectile.Position.y, 0f);
                return false;
            }

            // Офсет дула затухает к нулю — снаряд сходит на симовую траекторию задолго до импакта.
            _originOffset = Vector3.Lerp(_originOffset, Vector3.zero, 1f - Mathf.Exp(-OriginConvergeRate * Time.deltaTime));

            Vector2 p = Vector2.Lerp(_projectile.PreviousPosition, _projectile.Position, alpha);
            transform.position = new Vector3(p.x, p.y, 0f) + _originOffset;
            FaceVelocity(_projectile.Velocity);
            return true;
        }

        /// <summary>
        /// След под цвет источника — тот же тинт, что у тела снаряда, только ярче: хвост читается как
        /// свечение позади, а не как второй снаряд.
        /// <para>Clear ОБЯЗАТЕЛЕН: вид приходит из пула, и без сброса точек хвост тянется через весь экран
        /// от места, где кончился прошлый выстрел.</para>
        /// </summary>
        private void ApplyTrail(Color tint)
        {
            if (_trail == null) return;

            _trail.Clear();

            Color head = tint * _trailBrightness;
            head.a = tint.a;
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
