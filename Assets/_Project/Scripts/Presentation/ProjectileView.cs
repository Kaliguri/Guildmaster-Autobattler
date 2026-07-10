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

        private Projectile _projectile;

        /// <summary>Привязать к симовому снаряду и затинтовать под источник.</summary>
        public void Bind(Projectile projectile, Color tint)
        {
            _projectile = projectile;
            if (_sprite != null) _sprite.color = tint;
            transform.position = new Vector3(projectile.Position.x, projectile.Position.y, 0f);
            FaceVelocity(projectile.Velocity);
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
                transform.position = new Vector3(_projectile.Position.x, _projectile.Position.y, 0f);
                return false;
            }

            Vector2 p = Vector2.Lerp(_projectile.PreviousPosition, _projectile.Position, alpha);
            transform.position = new Vector3(p.x, p.y, 0f);
            FaceVelocity(_projectile.Velocity);
            return true;
        }

        // Спрайт нарисован «вправо»: поворачиваем по направлению полёта (в сторону цели).
        private void FaceVelocity(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
