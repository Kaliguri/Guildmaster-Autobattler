using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.Pool;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Пул и спавн боевых VFX-префабов (<see cref="PooledVfx"/>). Sorting layer/order и default-dir —
    /// из <see cref="VfxData"/>; относительный order детей — из префаба.
    /// </summary>
    public sealed class CombatVfx : MonoBehaviour
    {
        private readonly Dictionary<int, ObjectPool<PooledVfx>> _pools = new Dictionary<int, ObjectPool<PooledVfx>>();
        private readonly List<PooledVfx> _active = new List<PooledVfx>(32);

        /// <summary>
        /// Заспавнить VFX. <paramref name="dirDegOverride"/> = null → <see cref="VfxData.DefaultDirDeg"/>.
        /// <paramref name="intensity"/> множит <see cref="VfxData.Scale"/>.
        /// </summary>
        public void Spawn(VfxData data, Vector3 worldPos, float? dirDegOverride = null, float intensity = 1f)
        {
            if (data == null || data.Prefab == null) return;
            if (!data.Prefab.TryGetComponent(out PooledVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no PooledVfx on root.", data.Prefab);
                return;
            }

            int layerId = ResolveSortingLayerId(data.SortingLayerName);
            float dirDeg = dirDegOverride ?? data.DefaultDirDeg;
            float scale = data.Scale * Mathf.Max(0.01f, intensity);

            ObjectPool<PooledVfx> pool = GetOrCreatePool(data.Prefab);
            PooledVfx vfx = pool.Get();
            _active.Add(vfx);
            vfx.Play(worldPos, scale, dirDeg, layerId, data.SortingOrder, released =>
            {
                _active.Remove(released);
                pool.Release(released);
            });
        }

        /// <summary>Погасить всё летящее (battle reset) и вернуть в пулы.</summary>
        public void DespawnAll()
        {
            if (_active.Count == 0) return;
            PooledVfx[] snapshot = _active.ToArray();
            _active.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                if (snapshot[i] != null) snapshot[i].Cancel();
        }

        private static int ResolveSortingLayerId(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return 0;
            int id = SortingLayer.NameToID(layerName);
            if (id == 0 && layerName != "Default")
                Debug.LogWarning($"[CombatVfx] Sorting layer '{layerName}' not found — using Default.");
            return id;
        }

        private ObjectPool<PooledVfx> GetOrCreatePool(GameObject prefab)
        {
            int key = prefab.GetInstanceID();
            if (_pools.TryGetValue(key, out ObjectPool<PooledVfx> existing))
                return existing;

            var pool = new ObjectPool<PooledVfx>(
                createFunc: () =>
                {
                    GameObject go = Instantiate(prefab, transform);
                    go.name = prefab.name;
                    return go.GetComponent<PooledVfx>();
                },
                actionOnGet: v => v.gameObject.SetActive(true),
                actionOnRelease: v => v.gameObject.SetActive(false),
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 64);
            _pools[key] = pool;
            return pool;
        }
    }
}
