using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Пул призрачных копий тела (<see cref="GhostImage"/>): шлейф за рывком и иллюзия уклонения берут копии
    /// отсюда. Пул один на всех — копии одинаковы по устройству и отличаются только позой и цветом, поэтому
    /// делить их по юнитам смысла нет (в отличие от VFX, где у каждого эффекта свой префаб).
    /// </summary>
    public sealed class CombatGhosts : MonoBehaviour
    {
        private ObjectPool<GhostImage>  _pool;
        private readonly List<GhostImage> _active = new List<GhostImage>(32);
        private System.Action<GhostImage> _release;

        /// <summary>
        /// Оставить копию тела. Точка — мировые координаты ног, поза — снимок силуэта на этот момент.
        /// </summary>
        /// <param name="growTo">До какого масштаба копия вырастает: 1 — стоит (шлейф), больше — кольцо ряби.</param>
        /// <param name="delay">Пауза перед появлением, сек: ею разводятся во времени кольца ряби.</param>
        public void Leave(in UnitSilhouette silhouette, Vector3 feet, Color color, Material material,
                          int sortingLayerId, int sortingOrder, float life, float startAlpha,
                          float fadePower, float holo, float growTo = 1f, float delay = 0f)
        {
            if (!silhouette.Valid) return;

            _pool ??= CreatePool();
            _release ??= OnGhostDone;

            GhostImage ghost = _pool.Get();
            _active.Add(ghost);
            ghost.Play(in silhouette, feet, color, material, sortingLayerId, sortingOrder,
                       life, startAlpha, fadePower, holo, _release, growTo, delay);
        }

        /// <summary>Погасить все копии и вернуть их в пул (сброс боя, уход арены).</summary>
        public void DespawnAll()
        {
            if (_active.Count == 0) return;

            GhostImage[] snapshot = _active.ToArray();
            _active.Clear();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] == null) continue;
                snapshot[i].Stop();
                _pool?.Release(snapshot[i]);
            }
        }

        private void OnGhostDone(GhostImage ghost)
        {
            _active.Remove(ghost);
            _pool?.Release(ghost);
        }

        private ObjectPool<GhostImage> CreatePool() => new ObjectPool<GhostImage>(
            createFunc: () =>
            {
                var go = new GameObject("GhostImage");
                go.transform.SetParent(transform, worldPositionStays: false);
                return go.AddComponent<GhostImage>();
            },
            actionOnGet: g => g.gameObject.SetActive(true),
            actionOnRelease: g => g.gameObject.SetActive(false),
            actionOnDestroy: g => { if (g != null) Destroy(g.gameObject); },
            collectionCheck: false,
            defaultCapacity: 16,
            maxSize: 128);
    }
}
