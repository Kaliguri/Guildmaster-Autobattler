using Guildmaster.Presentation.Design;
using UnityEngine;
using UnityEngine.Pool;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Пул и спавн пиксельных боевых VFX-брызгов (<see cref="PixelBurst"/>). Создаётся презентером в рантайме
    /// (без правок сцены/префабов), брызги строятся кодом (общий меш+материал). Презентер зовёт
    /// <see cref="SpawnBurst"/> по боевым событиям в мировых точках-сокетах. Только презентация.
    /// </summary>
    public sealed class CombatVfx : MonoBehaviour
    {
        private ObjectPool<PixelBurst>   _pool;
        private System.Action<PixelBurst> _release;

        public void Initialize()
        {
            if (_pool != null) return;
            _release = b => _pool.Release(b);
            _pool = new ObjectPool<PixelBurst>(
                createFunc: () =>
                {
                    var go = new GameObject("PixelBurst");
                    go.transform.SetParent(transform, worldPositionStays: false);
                    return go.AddComponent<PixelBurst>();
                },
                actionOnGet:     b => b.gameObject.SetActive(true),
                actionOnRelease: b => b.gameObject.SetActive(false),
                actionOnDestroy: b => { if (b != null) Destroy(b.gameObject); },
                collectionCheck: false,
                defaultCapacity: 32,
                maxSize: 128);
        }

        /// <summary>Заспавнить брызг в мировой точке. dirDeg — направление конуса (для 360° неважен); intensity ∈ [0..1] режет кол-во.</summary>
        public void SpawnBurst(Vector3 worldPos, float dirDeg, PixelBurstPreset preset, float intensity, int sortingLayerId, int sortingOrder)
        {
            if (preset == null || preset.Count <= 0) return;
            Initialize();
            PixelBurst b = _pool.Get();
            b.Play(worldPos, dirDeg, preset, intensity, sortingLayerId, sortingOrder, _release);
        }
    }
}
