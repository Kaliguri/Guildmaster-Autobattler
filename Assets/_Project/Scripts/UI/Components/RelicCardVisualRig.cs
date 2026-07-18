using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Риг визуала пик-карточек: рендерит боевой <see cref="UnitData.ViewPrefab"/> юнита в
    /// <see cref="RenderTexture"/> для показа в <see cref="RelicCard"/> (план 10 §5 — анимированный
    /// спрайт вместо иконки). Скрытый «стейдж» далеко от игровой сцены + ортокамера НА КАРТОЧКУ
    /// (URP авто-рендерит включённые камеры в их targetTexture — ручной Camera.Render() в SRP не годится).
    /// Сам <c>UnitView</c> глушится (он сцеплен с симом) — Animator гоняется напрямую: Idle по умолчанию,
    /// Attack при выборе. Нет ViewPrefab → RT пустая (карточка сама падает на портрет-фолбэк).
    /// Владелец экрана держит риг и зовёт <see cref="Dispose"/> при закрытии.
    /// </summary>
    public sealed class RelicCardVisualRig : System.IDisposable
    {
        private static readonly int IdleHash   = Animator.StringToHash("Idle");
        private static readonly int AttackHash = Animator.StringToHash("Attack");

        private const float StageOrigin = 5000f; // далеко от игровой сцены (ничего не попадёт в кадр)
        private const float Spacing     = 6f;    // разнос юнитов, чтобы камера не ловила соседа

        private sealed class Entry
        {
            public GameObject    Unit;
            public Animator      Animator;
            public Camera        Camera;
            public RenderTexture Rt;
        }

        private readonly int _size;
        private readonly float _fallbackHeight;
        private readonly float _framePadding;
        private readonly Transform _root;
        private readonly List<Entry> _entries = new List<Entry>();
        private int _slot;

        /// <param name="size">Сторона квадратной RT в пикселях.</param>
        /// <param name="fallbackHeight">Высота кадра, если у юнита нет рекомендованного габарита.</param>
        /// <param name="framePadding">Запас над рекоменд-габаритом (1.2 = +20% воздуха вокруг фигуры).</param>
        public RelicCardVisualRig(int size = 256, float fallbackHeight = 2.0f, float framePadding = 1.2f)
        {
            _size = size;
            _fallbackHeight = fallbackHeight;
            _framePadding = framePadding;

            var rootGo = new GameObject("RelicCardVisualRig") { hideFlags = HideFlags.HideAndDontSave };
            rootGo.transform.position = new Vector3(StageOrigin, StageOrigin, 0f);
            _root = rootGo.transform;
        }

        /// <summary>Завести карточку под юнита; вернуть RT с его анимированным визуалом (idle).</summary>
        public RenderTexture Acquire(UnitData data)
        {
            var rt = new RenderTexture(_size, _size, 16, RenderTextureFormat.ARGB32) { name = "RelicCardRT" };
            rt.Create();

            Vector3 pos = _root.position + new Vector3(_slot * Spacing, 0f, 0f);
            _slot++;

            var entry = new Entry { Rt = rt };

            GameObject prefab = data != null ? data.ViewPrefab : null;
            if (prefab != null)
            {
                entry.Unit = Object.Instantiate(prefab, pos, Quaternion.identity, _root);

                // Глушим всю игровую логику вида (UnitView/бары — они сцеплены с симом). Animator/SpriteRenderer
                // — НЕ MonoBehaviour, остаются; их и гоняем.
                var behaviours = entry.Unit.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                    if (behaviours[i] != null) behaviours[i].enabled = false;

                HideWorldUi(entry.Unit);

                entry.Animator = entry.Unit.GetComponentInChildren<Animator>(true);
                if (entry.Animator != null)
                {
                    entry.Animator.enabled = true;
                    // Маркеры клипов — это ДАННЫЕ (ClipMarkers), не колбэки: глушим fireEvents, иначе Attack на
                    // ховере шлёт AnimationEvent 'Marker' в пустоту («has no receiver»). Тот же инвариант, что в UnitView.
                    entry.Animator.fireEvents = false;
                    entry.Animator.Play(IdleHash, 0, 0f);
                }
            }

            // Камера НА этот юнит: рендерит в свою RT автоматически (URP).
            // ВАЖНО: камера рождается disabled и включается ТОЛЬКО после назначения targetTexture —
            // enabled-камера без RT рисует на экран (Display 0). Тот же инвариант держим в Dispose.
            var camGo = new GameObject("RigCamera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(_root, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled          = false;                     // не рендерить, пока нет targetTexture
            cam.depth            = -100f;                      // страховка: даже без RT не перекроет игровую камеру
            cam.orthographic     = true;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0f, 0f, 0f, 0f); // прозрачный фон — карточка просвечивает
            cam.nearClipPlane    = 0.01f;
            cam.farClipPlane     = 100f;
            cam.targetTexture    = rt;
            FrameByRecommendedSize(cam, entry.Unit, pos); // кадрируем по гизмо-габариту юнита
            cam.enabled          = true;                      // RT назначена — теперь можно рендерить
            entry.Camera = cam;

            _entries.Add(entry);
            return rt;
        }

        /// <summary>Проиграть анимацию атаки (выбор карточки).</summary>
        public void PlayAttack(RenderTexture rt)
        {
            Entry e = Find(rt);
            if (e?.Animator != null) e.Animator.Play(AttackHash, 0, 0f);
        }

        /// <summary>
        /// Длина клипа атаки (сек) — чтобы вызывающий вернул карту в idle РОВНО после проигрыша атаки
        /// (клип не имеет exit-перехода в Idle, иначе застревает на последнем кадре). 0 — нет клипа/аниматора.
        /// </summary>
        public float AttackLengthSeconds(RenderTexture rt)
        {
            Entry e = Find(rt);
            var ctrl = e?.Animator != null ? e.Animator.runtimeAnimatorController : null;
            if (ctrl == null) return 0f;
            foreach (AnimationClip clip in ctrl.animationClips)
                if (clip != null && clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip.length;
            return 0f;
        }

        /// <summary>
        /// Заморозить/разморозить карточку: замороженная стоит на первом кадре Idle (speed 0) — статична;
        /// размороженная гоняет анимацию (speed 1). Нужно, чтобы двигался ТОЛЬКО выбранный юнит, а не все.
        /// </summary>
        public void SetFrozen(RenderTexture rt, bool frozen)
        {
            Entry e = Find(rt);
            if (e?.Animator == null) return;
            if (frozen) { e.Animator.Play(IdleHash, 0, 0f); e.Animator.speed = 0f; }
            else e.Animator.speed = 1f;
        }

        /// <summary>Вернуть карточку в idle.</summary>
        public void PlayIdle(RenderTexture rt)
        {
            Entry e = Find(rt);
            if (e?.Animator != null) e.Animator.Play(IdleHash, 0, 0f);
        }

        private Entry Find(RenderTexture rt)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Rt == rt) return _entries[i];
            return null;
        }

        // Кадрирование по рекомендованному габариту юнита (гизмо UnitView: _recommendedHeight + _feetPoint),
        // чтобы фигура была одинаково крупной и по центру независимо от кадра анимации. UI-асмдеф не ссылается
        // на Presentation → читаем поля рефлексией по имени компонента.
        private void FrameByRecommendedSize(Camera cam, GameObject unit, Vector3 spawnPos)
        {
            float height = _fallbackHeight;
            float feetX = spawnPos.x;
            float feetY = spawnPos.y;

            if (unit != null)
            {
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var behaviours = unit.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour mb = behaviours[i];
                    if (mb == null || mb.GetType().Name != "UnitView") continue;
                    System.Type t = mb.GetType();
                    var fh = t.GetField("_recommendedHeight", F);
                    if (fh != null && fh.GetValue(mb) is float h && h > 0.01f) height = h;
                    var ff = t.GetField("_feetPoint", F);
                    if (ff != null && ff.GetValue(mb) is Transform feet && feet != null)
                    {
                        feetX = feet.position.x;
                        feetY = feet.position.y;
                    }
                    break;
                }
            }

            float framed = Mathf.Max(0.2f, height * _framePadding);
            cam.orthographicSize = framed * 0.5f;
            // Центр кадра = ноги + половина рамки: ноги у низа, макушка под верхней кромкой.
            cam.transform.position = new Vector3(feetX, feetY + framed * 0.5f, -10f);
        }

        // Грубо гасим world-UI юнита (бары/подпись/контейнер 'UI') — на карточке нужен только персонаж.
        private static void HideWorldUi(GameObject unit)
        {
            var transforms = unit.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                string n = transforms[i].name.ToLowerInvariant();
                if (n == "ui" || n.Contains("bar") || n.Contains("label") || n.Contains("canvas"))
                    transforms[i].gameObject.SetActive(false);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];

                // Порядок критичен (см. коммент рига): гасим камеру ПЕРЕД снятием RT, чтобы ни одного
                // кадра не было enabled-камеры без targetTexture (иначе она рисует юнита на экран и залипает).
                if (e.Camera != null)
                {
                    e.Camera.enabled = false;
                    e.Camera.targetTexture = null;
                    DestroyObject(e.Camera.gameObject);
                }

                // Гасим Animator перед уничтожением юнита — иначе его PlayableGraph утекает
                // ("PlayableGraph was not destroyed").
                if (e.Animator != null) e.Animator.enabled = false;
                if (e.Unit != null) DestroyObject(e.Unit);

                if (e.Rt != null) { e.Rt.Release(); DestroyObject(e.Rt); }
            }
            _entries.Clear();
            if (_root != null) DestroyObject(_root.gameObject);
        }

        // В play-mode отложенный Destroy ок; в edit-mode (превью-стенд, UI Builder) Object.Destroy НЕ
        // уничтожает — нужен DestroyImmediate, иначе камера-риг остаётся в сцене намертво до перезапуска.
        private static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
