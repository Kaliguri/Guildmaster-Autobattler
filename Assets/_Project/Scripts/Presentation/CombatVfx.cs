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
        // Ключ пула — EntityId префаба, а не int: приведение EntityId к int Unity объявила уходящим.
        private readonly Dictionary<EntityId, ObjectPool<PooledVfx>> _pools = new Dictionary<EntityId, ObjectPool<PooledVfx>>();
        private readonly List<PooledVfx> _active = new List<PooledVfx>(32);

        /// <summary>
        /// Заспавнить VFX. <paramref name="dirDegOverride"/> = null → <see cref="VfxData.DefaultDirDeg"/>.
        /// </summary>
        /// <param name="sizeMultiplier">
        /// Единственный рантайм-множитель РАЗМЕРА: сила удара. Базовый размер живёт в
        /// <see cref="VfxData.SizeUnits"/> и здесь не дублируется — множителей размера в проекте ровно
        /// два, и оба видны в этой строке.
        /// </param>
        /// <param name="countScale">Множитель КОЛИЧЕСТВА частиц в бёрстах: вес удара читается частотой искр.</param>
        /// <param name="tint">Палитра владельца (<c>UnitData.ResolveVfxGradient</c>) — ДИАПАЗОН для разброса; null = как в префабе.</param>
        /// <param name="wound">
        /// Показывать ли поток вскрытого (медленные красные искры). <c>false</c> — удар принял щит: тело
        /// целое, и красным взяться неоткуда.
        /// </param>
        public void Spawn(VfxData data, Vector3 worldPos, float? dirDegOverride = null, float sizeMultiplier = 1f,
                          float countScale = 1f, Gradient tint = null, bool wound = true,
                          string slot = null)
        {
            if (!Ready(data, slot)) return;
            if (!data.Prefab.TryGetComponent(out PooledVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no PooledVfx on root.", data.Prefab);
                return;
            }

            int layerId = ResolveSortingLayerId(data.SortingLayerName);
            float dirDeg = dirDegOverride ?? data.DefaultDirDeg;
            float sizeUnits = data.SizeUnits * Mathf.Max(0.01f, sizeMultiplier);

            ObjectPool<PooledVfx> pool = GetOrCreatePool(data.Prefab);
            PooledVfx vfx = pool.Get();
            _active.Add(vfx);
            vfx.Play(worldPos, sizeUnits, dirDeg, layerId, data.SortingOrder, released =>
            {
                _active.Remove(released);
                pool.Release(released);
            }, countScale, tint, lifeOverride: 0f, wound: wound);
        }

        /// <summary>
        /// Заспавнить ФОРМУ УДАРА — эффект, которому мало одной точки: он строится по A→B и несёт свои
        /// параметры генерации. Пул, sorting и возврат — общие с остальными VFX, поэтому форма едет тем
        /// же швом, а не вторым каналом рядом с ним.
        /// </summary>
        /// <remarks>
        /// Размер сюда не приходит множителем: у формы он уже посчитан в
        /// <see cref="Effects.HitFormParams.Length"/> — вес удара выражается длиной, а не масштабом
        /// префаба, и второй владелец размера здесь был бы прямым нарушением контракта <c>VfxData</c>.
        /// </remarks>
        public void SpawnForm(VfxData data, in Effects.HitFormParams form, string slot = null)
        {
            if (!Ready(data, slot)) return;
            if (!data.Prefab.TryGetComponent(out PooledVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no PooledVfx on root.", data.Prefab);
                return;
            }
            if (!data.Prefab.TryGetComponent(out Effects.HitFormVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no HitFormVfx — " +
                               "форму рисовать нечем.", data.Prefab);
                return;
            }

            int layerId = ResolveSortingLayerId(data.SortingLayerName);
            ObjectPool<PooledVfx> pool = GetOrCreatePool(data.Prefab);
            PooledVfx vfx = pool.Get();
            _active.Add(vfx);

            // Жизнь считаем сами: частиц у формы нет, а срок ей продлевает заморозка hitstop — вывести
            // его из префаба было бы неоткуда.
            // Позицию, поворот и масштаб сразу после этого перезапишет сама форма: её геометрия считается
            // из A→B, а не из одной точки и угла. Здесь Play нужен ради пула, sorting и возврата.
            float life = form.Life + form.FreezeSeconds;
            vfx.Play(form.To, sizeUnits: 1f, dirDeg: 0f, layerId, data.SortingOrder, released =>
            {
                _active.Remove(released);
                pool.Release(released);
            }, lifeOverride: life);

            // Компонент ищем на экземпляре, а не держим словарём: форма спавнится раз в удар, и словарь
            // здесь стоил бы больше, чем экономил.
            if (vfx.TryGetComponent(out Effects.HitFormVfx view)) view.Apply(form);
        }

        /// <summary>
        /// Заспавнить ДУГУ ЗА КЛИНКОМ — эффект, живущий весь взмах и следящий за плечом бьющего. В пул
        /// возвращается сам, догорев: длину взмаха заранее не знает никто, её ведёт скраб по сим-тикам.
        /// </summary>
        /// <param name="source">Кто машет — у него дуга спрашивает геометрию каждый кадр.</param>
        public void SpawnArc(VfxData data, Effects.ISwingArcSource source, Color colour,
                             float innerShare, float tailBias, float fadeOutSeconds, string slot = null)
        {
            if (!Ready(data, slot) || source == null) return;
            if (!data.Prefab.TryGetComponent(out PooledVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no PooledVfx on root.", data.Prefab);
                return;
            }
            if (!data.Prefab.TryGetComponent(out Effects.SwingArcVfx _))
            {
                Debug.LogError($"[CombatVfx] Prefab '{data.Prefab.name}' for '{data.Id}' has no SwingArcVfx — " +
                               "дугу вести нечем.", data.Prefab);
                return;
            }

            int layerId = ResolveSortingLayerId(data.SortingLayerName);
            ObjectPool<PooledVfx> pool = GetOrCreatePool(data.Prefab);
            PooledVfx vfx = pool.Get();
            _active.Add(vfx);

            // Срок — страховка от зависшей дуги (юнит умер посреди взмаха, вид переиспользовали), а не
            // её настоящая жизнь: нормальный путь — самостоятельный возврат по завершении.
            vfx.Play(Vector3.zero, sizeUnits: 1f, dirDeg: 0f, layerId, data.SortingOrder, released =>
            {
                _active.Remove(released);
                pool.Release(released);
            }, lifeOverride: ArcSafetyLifetime);

            if (vfx.TryGetComponent(out Effects.SwingArcVfx arc))
                arc.Begin(source, colour, innerShare, tailBias, fadeOutSeconds);
        }

        /// <summary>Потолок жизни дуги, сек: страховка на случай, если взмах оборвался вместе с юнитом.</summary>
        private const float ArcSafetyLifetime = 4f;

        /// <summary>
        /// Есть ли чем рисовать этот эффект. Незаполненный слот в feel-конфиге — дефект разводки, а не
        /// «эффект выключен»: у выключения есть свой тумблер, и он спрашивается ДО вызова.
        /// </summary>
        /// <param name="slot">
        /// Имя поля, которым эффект попросили (<c>VfxSwingArc</c>): в консоли обязано быть видно
        /// НЕЗАПОЛНЕННОЕ ПОЛЕ, а не безымянный «какой-то VFX». Передаётся вызывающим через
        /// <c>nameof</c> — компилятор подставить его не может (<c>CallerArgumentExpression</c> в этой
        /// версии рантайма недоступен).
        /// </param>
        private static bool Ready(VfxData data, string slot)
        {
            if (data != null && data.Prefab != null) return true;

            VisualDefects.Report($"vfx-slot:{slot ?? "?"}",
                $"[CombatVfx] эффект '{slot ?? "слот не назван"}' не разведён: " +
                (data == null ? "поле пустое" : $"у VfxData '{data.Id}' нет префаба") +
                " — показывать этот удар нечем.", data);
            return false;
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
            EntityId key = prefab.GetEntityId();
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
