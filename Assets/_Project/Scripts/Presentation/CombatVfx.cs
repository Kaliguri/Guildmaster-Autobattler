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
            vfx.Play(form.At, sizeUnits: 1f, dirDeg: 0f, layerId, data.SortingOrder, released =>
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
        /// <param name="feel">Feel-конфиг: все числа дуги раскладывает <see cref="Effects.SwingArcLaunch"/>.</param>
        /// <param name="unitGlow">Цвет свечения бьющего; свою яркость дуга домножает сама, внутри Launch.</param>
        public void SpawnArc(VfxData data, Effects.ISwingArcSource source,
                             Design.CombatFeelConfig feel, Color unitGlow, string slot = null)
        {
            // Тумблер спрашивается ПЕРВЫМ: выключенная дуга не занимает объект пула и не жалуется на
            // незаполненный слот в данных — «выключено» и «не разведено» это разные новости.
            if (!Effects.SwingArcLaunch.Enabled(feel) || source == null) return;
            if (!Ready(data, slot)) return;
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

            // Слой и порядок из данных легли выше — но если у бьющего есть место ВНУТРИ тела, дуга туда и
            // переедет: это решает сам эффект, спросив источник (см. SwingArcVfx.Anchor).
            if (vfx.TryGetComponent(out Effects.SwingArcVfx arc))
                Effects.SwingArcLaunch.Begin(arc, feel, source, unitGlow);
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

        /// <summary>
        /// Подобрать эффекты, которые выключили ЧУЖОЙ рукой: вернуть их в пул, раз тика у них больше
        /// не будет.
        /// </summary>
        /// <remarks>
        /// <b>Страховка по сроку живёт в <c>Update</c> самого эффекта, а выключенный объект не
        /// тикает</b> — вместе с тиком замирает и она. Эффект остаётся «играющим» навсегда: пул его не
        /// получил, показывать некому, а при следующем включении он оживает с прежним состоянием.
        /// <para>Ловится это на дуге за клинком: она переезжает ВНУТРЬ тела бьющего (см. родителя в
        /// <see cref="SpawnArc"/>), а вид юнита уходит в свой пул вместе со смертью. Дуга гаснет
        /// ребёнком, срок стоит — а когда вид переиспользуют под нового бойца, застрявший росчерк
        /// появляется снова, и у каждого игрока свой: смерти на машинах разные (наход. Макса
        /// 08.08.2026, «остается просто видеть и не исчезает ... это не сихронизировано»).</para>
        /// <para><b>Сторожит владелец пула, а не сам эффект.</b> Возврат по <c>OnDisable</c> выглядел
        /// короче, но опирался бы на чужую руку — на того, кто выключил родителя. Пул живёт всегда, и
        /// спросить «а этот ещё играет?» может только он. Заодно это проверяемо тестом: в EditMode вне
        /// Play Mode <c>OnDisable</c> ребёнку при выключении родителя не приходит вовсе.</para>
        /// <para>Идём с конца: возврат в пул снимает эффект с этого же списка.</para>
        /// </remarks>
        public void ReclaimOrphans()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;   // список мог укоротиться возвратом соседа

                PooledVfx vfx = _active[i];
                if (vfx == null) { _active.RemoveAt(i); continue; }
                if (vfx.gameObject.activeInHierarchy || !vfx.IsPlaying) continue;

                vfx.Cancel();   // вернётся в пул сам и снимет себя со списка
            }
        }

        private void Update() => ReclaimOrphans();

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
                // Родитель возвращается ВСЕГДА, а не только тем, кто его менял: дуга за клинком переезжает
                // внутрь тела бьющего, и объект, оставшийся его ребёнком, умрёт вместе с видом — то есть
                // исчезнет из пула молча, а пул будет считать, что вернул его себе.
                actionOnRelease: v =>
                {
                    v.gameObject.SetActive(false);
                    if (v.transform.parent != transform) v.transform.SetParent(transform, worldPositionStays: false);
                },
                actionOnDestroy: v => { if (v != null) Destroy(v.gameObject); },
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 64);
            _pools[key] = pool;
            return pool;
        }
    }
}
