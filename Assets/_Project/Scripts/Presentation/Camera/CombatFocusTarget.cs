using Guildmaster.Combat;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Цель слежения для экшн-камеры (вики «16» §5): каждый кадр ставит свой transform в
    /// сглаженный центроид живых юнитов и считает «разброс» боя (для динамического зума).
    /// Только читает симуляцию, ничего не мутирует. Экшн-камера Cinemachine следует за этим
    /// transform (поле Follow), а <see cref="CameraModeController"/> подгоняет орто-размер под
    /// <see cref="Spread"/>.
    /// </summary>
    public sealed class CombatFocusTarget : MonoBehaviour
    {
        [Tooltip("Скорость сглаживания позиции (больше — резче следует за центром боя).")]
        [SerializeField] private float _positionDamping = 4f;

        [Tooltip("Скорость сглаживания разброса (для зума). Меньше — плавнее реакция зума на смерть/рывок юнита.")]
        [SerializeField] private float _spreadDamping = 2.5f;

        private CombatSimulation _simulation;
        // На первом кадре с юнитами снапаем центр/разброс, чтобы камера не «наезжала» рывком со старта.
        private bool _initialized;

        /// <summary>Есть ли живые юниты в кадре (иначе центроид/разброс не обновляются).</summary>
        public bool HasUnits { get; private set; }

        /// <summary>Полу-разброс боя: макс. расстояние живого юнита от центроида (для подгона зума).</summary>
        public float Spread { get; private set; }

        [Inject]
        public void Construct(CombatSimulation simulation) => _simulation = simulation;

        private void LateUpdate()
        {
            if (_simulation == null) return;

            var units = _simulation.Units;
            Vector2 sum = Vector2.zero;
            int alive = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsDead) continue;
                sum += units[i].Position;
                alive++;
            }

            HasUnits = alive > 0;
            if (!HasUnits) return;

            Vector2 centroid = sum / alive;

            float maxSqr = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].IsDead) continue;
                float d = (units[i].Position - centroid).sqrMagnitude;
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

            // Экспоненциальное сглаживание, независимое от частоты кадров. Разброс сглаживаем отдельно:
            // сырой скачет при смерти/рывке (телепорт монаха, отбрасывание) и дёргал бы зум.
            float tPos    = 1f - Mathf.Exp(-_positionDamping * Time.deltaTime);
            float tSpread = 1f - Mathf.Exp(-_spreadDamping   * Time.deltaTime);
            Spread = Mathf.Lerp(Spread, rawSpread, tSpread);
            transform.position = Vector3.Lerp(cur, centroid3, tPos);
        }
    }
}
