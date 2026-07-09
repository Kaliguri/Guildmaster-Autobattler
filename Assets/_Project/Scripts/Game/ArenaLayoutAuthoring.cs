using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Guildmaster.Game
{
    /// <summary>
    /// Авторинг арены в сцене/префабе: задаёт границы поля и зоны расстановки, рисует их гизмо.
    /// На старте боя печёт неизменяемый снапшот (<see cref="BuildLayout"/> → <see cref="ArenaLayoutData"/>)
    /// для симуляции и расстановки. Источник правды в рантайме — снапшот, не этот компонент.
    /// <para>prefab-per-arena (вики «15» §4): компонент лежит в корне арена-префаба, визуал — в
    /// дочернем объекте. Геометрия ведёт, арт следует — рамки правятся хэндлами прямо в Scene-вью
    /// (см. <c>ArenaLayoutAuthoringEditor</c>).</para>
    /// </summary>
    public sealed class ArenaLayoutAuthoring : MonoBehaviour
    {
        [Serializable]
        private struct ZoneAuthoring
        {
            [HorizontalGroup("side"), LabelWidth(44), LabelText("Кто")]
            public DeploymentSide Side;

            [HorizontalGroup("side"), LabelWidth(50), LabelText("Ранг")]
            public DeploymentTier Tier;

            [LabelText("Центр (лок.)")]
            public Vector2 Center;

            [LabelText("Размер")]
            public Vector2 Size;
        }

        [Title("Границы поля")]
        [Tooltip("Центр боевого поля относительно этого объекта.")]
        [LabelText("Центр (лок.)")]
        [SerializeField] private Vector2 _boundsCenter = Vector2.zero;

        [Tooltip("Полный размер поля (ширина, высота).")]
        [LabelText("Размер")]
        [SerializeField] private Vector2 _boundsSize = new Vector2(20f, 12f);

        [Title("Зоны расстановки")]
        [InfoBox("Тяни рамки в Scene-вью за грани/углы. Белая — поле, синяя — игрок, красная — враг, " +
                 "полупрозрачная — Extended. Кнопкой «+» зона добавляется видимого размера на своей половине.")]
        [ListDrawerSettings(CustomAddFunction = nameof(NewZone), DraggableItems = true, ShowFoldout = false)]
        [SerializeField] private ZoneAuthoring[] _zones = Array.Empty<ZoneAuthoring>();

        /// <summary>Спечь неизменяемый снапшот арены в мировых координатах.</summary>
        public ArenaLayoutData BuildLayout()
        {
            Vector2 origin = transform.position;

            var bounds = new ArenaBounds(origin + _boundsCenter, _boundsSize);

            var zones = new List<DeploymentZone>(_zones.Length);
            for (int i = 0; i < _zones.Length; i++)
            {
                ZoneAuthoring z = _zones[i];
                zones.Add(new DeploymentZone(new Rect2D(origin + z.Center, z.Size), z.Side, z.Tier));
            }

            return new ArenaLayoutData(bounds, zones);
        }

        // Дефолт для кнопки «+» списка (Odin CustomAddFunction): зона видимого размера на половине
        // игрока, а не нулевой default(struct) (иначе гизмо не видно — жалоба на «настройку зон»).
        private ZoneAuthoring NewZone()
        {
            float w = Mathf.Abs(_boundsSize.x);
            float h = Mathf.Abs(_boundsSize.y);
            return new ZoneAuthoring
            {
                Side   = DeploymentSide.Player,
                Tier   = DeploymentTier.Normal,
                Center = _boundsCenter + new Vector2(-w * 0.25f, 0f),
                Size   = new Vector2(w * 0.3f, h * 0.9f),
            };
        }

        private void OnDrawGizmos()
        {
            Vector2 origin = transform.position;

            // Границы поля — белым.
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(origin + _boundsCenter, _boundsSize);

            for (int i = 0; i < _zones.Length; i++)
            {
                ZoneAuthoring z = _zones[i];
                Color c = z.Side == DeploymentSide.Player
                    ? new Color(0.3f, 0.7f, 1f)   // синий — игрок
                    : new Color(1f, 0.4f, 0.3f);  // красный — враг
                if (z.Tier == DeploymentTier.Extended) c.a = 0.5f; // extended — полупрозрачным контуром
                Gizmos.color = c;
                Gizmos.DrawWireCube(origin + z.Center, z.Size);
            }
        }
    }
}
