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

        private CombatSimulation _simulation;

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
            Spread = Mathf.Sqrt(maxSqr);

            // Экспоненциальное сглаживание, независимое от частоты кадров.
            float t = 1f - Mathf.Exp(-_positionDamping * Time.deltaTime);
            Vector3 cur = transform.position;
            var target = new Vector3(centroid.x, centroid.y, cur.z);
            transform.position = Vector3.Lerp(cur, target, t);
        }
    }
}
