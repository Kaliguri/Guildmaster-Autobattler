using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Тело из ЧАСТЕЙ — скелетный юнит: полтора десятка <see cref="SpriteRenderer"/> под одним корнем.
    /// Ставится на корень визуала (тот же узел, что несёт <see cref="Animator"/>) и является для вида
    /// единственным собеседником по телу: красит, вспыхивает, разворачивает, сортирует, снимает силуэт и
    /// колет на осколки — всеми частями сразу.
    /// <para>
    /// Компонент ВЛАДЕЕТ ПОРЯДКОМ ОТРИСОВКИ: список <c>_parts</c> читается сверху вниз, как слои в
    /// Photoshop (верхний элемент рисуется поверх), и раскладывается в <c>sortingOrder</c> кнопкой
    /// инспектора. До этого порядок жил в <c>m_SortingOrder</c> каждого спрайта по отдельности — увидеть
    /// его целиком было негде, а редактировать приходилось в шестнадцати местах.
    /// </para>
    /// <para>
    /// Порядок раскладывается ВНУТРИ <see cref="SortingGroup"/>: снаружи юнит остаётся для Y-сортировки
    /// арены одним объектом, внутри действует наш порядок.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkeletalBodyVisual : MonoBehaviour, IUnitBodyVisual
    {
        [Tooltip("Части тела в порядке отрисовки: ВЕРХНИЙ рисуется поверх остальных. Перетаскиванием меняется " +
                 "порядок, кнопка «Применить порядок» раскладывает его в sortingOrder внутри группы.")]
        [SerializeField] private List<SpriteRenderer> _parts = new();

        [Tooltip("Группа сортировки юнита: через неё тело получает Y-sort арены ОДНОЙ записью, сохраняя " +
                 "внутренний порядок частей. Пусто — берётся с этого объекта или из родителей.")]
        [SerializeField] private SortingGroup _group;

        private MaterialPropertyBlock _mpb;
        private BodyVisualState       _lastState;
        private bool                  _effectApplied;
        private Transform[]           _partTransforms;   // кэш: позы частей опрашиваются каждый кадр силуэтом/границами
        private PartRole[]            _partRoles;        // кэш ролей частей (метка UnitPartRole на узле) — для адресного свечения
        private bool                  _groupWarned;      // про отсутствие группы говорим один раз, а не каждый кадр

        /// <summary>Части тела в порядке отрисовки (только чтение — владелец порядка это компонент).</summary>
        public IReadOnlyList<SpriteRenderer> Parts => _parts;

        private void Awake()
        {
            if (_group == null) _group = GetComponent<SortingGroup>() ?? GetComponentInParent<SortingGroup>(true);

            // Ни одной живой части — тела у нас нет. Собираем из иерархии, чтобы юнит не вышел на арену
            // телом, которое нечем покрасить и нечем разбить, но молчать об этом нельзя: без списка мы не
            // знаем задуманного порядка частей и берём тот, в котором их вернуло дерево.
            //
            // Считаем ЖИВЫЕ, а не длину: список из шестнадцати оборванных ссылок не пуст, и проверка на
            // Count пропустила бы его молча. Так и случилось 31.07.2026, когда арт переехал в контейнеры
            // «Visual Part (Кость)» — узлы спрайтов пересоздались, ссылки вида умерли все разом, и тело
            // перестало краситься, вспыхивать, отбрасывать силуэт и колоться на осколки без единой строки
            // в консоли.
            if (LiveParts() == 0)
            {
                int lost = _parts.Count;
                CollectParts(_parts);
                Debug.LogError($"[SkeletalBodyVisual] {name}: ни одной живой части " +
                               $"({lost} ссылок были потеряны) — список собран из иерархии ({_parts.Count} шт.) " +
                               "в порядке дерева. Разведи порядок кнопкой «Собрать заново» в инспекторе, " +
                               "иначе части рисуются в случайном порядке.", this);
            }

            CachePartTransforms();
        }

        /// <summary>Сколько ссылок в списке ещё указывают на существующий рендерер.</summary>
        private int LiveParts()
        {
            int live = 0;
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null) live++;
            return live;
        }

        private void CachePartTransforms()
        {
            _partTransforms = new Transform[_parts.Count];
            _partRoles      = new PartRole[_parts.Count];
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                _partTransforms[i] = part != null ? part.transform : null;
                // Метка роли лежит на том же узле, что рендерер части (ставится в префабе, фаза A). Нет
                // метки — часть обычная (Body) и на касте не светится.
                var tag = part != null ? part.GetComponent<UnitPartRole>() : null;
                _partRoles[i] = tag != null ? tag.Role : PartRole.Body;
            }
        }

        public bool HasContent
        {
            get
            {
                for (int i = 0; i < _parts.Count; i++)
                    if (_parts[i] != null && _parts[i].sprite != null) return true;
                return false;
            }
        }

        /// <summary>Корень частей: его масштаб несёт сплющивание и знак разворота.</summary>
        public Transform Root => transform;

        public int SortingLayerId => _group != null
            ? _group.sortingLayerID
            : (_parts.Count > 0 && _parts[0] != null ? _parts[0].sortingLayerID : 0);

        public int SortingOrder => _group != null ? _group.sortingOrder : 0;

        public bool IsFlippedX => transform.localScale.x < 0f;

        public void Prime(Color flashColor)
        {
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;
                part.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BodyShaderIds.FlashAmount, 0f);
                _mpb.SetColor(BodyShaderIds.FlashColor, flashColor);
                _mpb.SetFloat(BodyShaderIds.Holo, 0f);
                _mpb.SetFloat(BodyShaderIds.Outline, 0f);
                _mpb.SetFloat(BodyShaderIds.GlowAmount, 0f);
                part.SetPropertyBlock(_mpb);
            }
            _effectApplied = false;
            _lastState     = default;
        }

        public void Apply(in BodyVisualState state)
        {
            // Тинт и альфа инвиза — каждой части: полупрозрачным должно стать тело, а не грудь.
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part != null) part.color = state.Tint;
            }

            bool active = state.HasEffect;
            if (!active && !_effectApplied) return;
            if (active && _effectApplied && state.Equals(_lastState)) return;

            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;
                PartRole role = _partRoles != null && i < _partRoles.Length ? _partRoles[i] : PartRole.Body;
                bool partGlows = (role & state.GlowRoles) != 0;
                part.GetPropertyBlock(_mpb);
                BodyShaderIds.Write(_mpb, state, part.sprite, partGlows);
                part.SetPropertyBlock(_mpb);
            }

            _effectApplied = active;
            _lastState     = state;
        }

        /// <summary>Ордер получает ГРУППА: внутри неё порядок частей остаётся нашим.</summary>
        public void SetSortingOrder(int order)
        {
            if (_group != null) { _group.sortingOrder = order; return; }

            // Ругаемся здесь, а не в Awake: без группы тело всё равно живёт (стенд анимаций им и пользуется),
            // но как только КТО-ТО начинает раздавать Y-sort — значит тело вышло на арену, и там его нечем
            // отсортировать целиком.
            if (_groupWarned) return;
            _groupWarned = true;
            Debug.LogError($"[SkeletalBodyVisual] {name}: нет SortingGroup ни на объекте, ни в родителях — " +
                           "Y-сортировка арены не дойдёт до тела, и юнит будет тонуть в чужих частях.", this);
        }

        public void SetFlipX(bool flip)
        {
            Vector3 s = transform.localScale;
            float mag = Mathf.Abs(s.x) > 1e-5f ? Mathf.Abs(s.x) : 1f;
            s.x = flip ? -mag : mag;
            transform.localScale = s;
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null || part.sprite == null || !part.enabled) continue;
                if (!any) { bounds = part.bounds; any = true; }
                else bounds.Encapsulate(part.bounds);
            }
            return any;
        }

        public UnitSilhouette CaptureSilhouette(Vector2 feet)
        {
            if (_partTransforms == null || _partTransforms.Length != _parts.Count) CachePartTransforms();

            var parts = new List<SilhouettePart>(_parts.Count);
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null || part.sprite == null || !part.enabled) continue;

                Transform t = _partTransforms[i];
                Vector3 offset = t.position - new Vector3(feet.x, feet.y, t.position.z);
                // Полная поза, а не «офсет плюс масштаб»: части повёрнуты клипом, а разворот тела сидит
                // отрицательным масштабом корня и приезжает сюда через lossyScale.
                var local = Matrix4x4.TRS(offset, t.rotation, t.lossyScale);
                parts.Add(new SilhouettePart(part.sprite, local, part.flipX, i));
            }

            return parts.Count > 0 ? new UnitSilhouette(parts.ToArray()) : UnitSilhouette.None;
        }

        public void SetVisible(bool visible)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i] != null) _parts[i].enabled = visible;
        }

        /// <summary>
        /// Колется КАЖДАЯ часть — общей палитрой и общим таймингом. Тело разлетается по частям, а не одним
        /// прямоугольником торса, и это заодно честнее: у составного юнита «прямоугольник тела» никогда не
        /// совпадал с фигурой.
        /// </summary>
        public void PlayShatter(Design.CombatFeelConfig feel, Gradient palette, System.Action onComplete)
        {
            int pending = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part != null && part.sprite != null && part.enabled) pending++;
            }

            if (pending == 0) { onComplete?.Invoke(); return; }

            int left = pending;
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null || part.sprite == null || !part.enabled) continue;

                var go = new GameObject("DeathShatter");
                // Сиблинг САМОЙ ЧАСТИ: осколки руки должны стартовать в пространстве руки — с её поворотом
                // от клипа и её масштабом, иначе разлёт уезжает от того места, где часть была видна.
                go.transform.SetParent(part.transform.parent != null ? part.transform.parent : transform,
                    worldPositionStays: false);
                var shatter = go.AddComponent<DeathShatter>();
                shatter.Play(part, feel, palette, () =>
                {
                    // Догорел последний осколок последней части — тело отработало целиком.
                    if (--left == 0) onComplete?.Invoke();
                });

                part.enabled = false;
            }
        }

        // --- Разводка (редактор) --------------------------------------------------------------------
        // Живёт в компоненте, а не только в его инспекторе: те же операции нужны валидатору рига и
        // сборщику префабов, и второй копии этой логики быть не должно.

        /// <summary>
        /// Собрать части из иерархии в <paramref name="into"/> в порядке обхода дерева. Публично ради
        /// редактора и валидатора; порядок отрисовки этим НЕ решается — он остаётся за списком.
        /// </summary>
        public void CollectParts(List<SpriteRenderer> into)
        {
            into.Clear();
            GetComponentsInChildren(true, into);
        }

        /// <summary>
        /// Пересканировать иерархию: новые части падают в конец списка, исчезнувшие и потерянные ссылки
        /// вычищаются, дубли снимаются. Порядок уже разведённых частей сохраняется.
        /// </summary>
        /// <returns>true — список изменился.</returns>
        public bool RebuildParts()
        {
            var found = new List<SpriteRenderer>();
            CollectParts(found);

            var merged = new List<SpriteRenderer>(found.Count);
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null || !found.Contains(part) || merged.Contains(part)) continue;
                merged.Add(part);   // уже разведённая часть — держим её место в порядке
            }
            for (int i = 0; i < found.Count; i++)
                if (!merged.Contains(found[i])) merged.Add(found[i]);

            bool changed = merged.Count != _parts.Count;
            if (!changed)
                for (int i = 0; i < merged.Count; i++)
                    if (merged[i] != _parts[i]) { changed = true; break; }

            if (changed)
            {
                _parts.Clear();
                _parts.AddRange(merged);
                CachePartTransforms();
            }
            return changed;
        }

        /// <summary>
        /// Переставить список по ТЕКУЩЕМУ <c>sortingOrder</c> частей — большой ордер наверх. Это переезд
        /// порядка от старого владельца (поле на каждом спрайте) к новому (этот список): разводка, настроенная
        /// руками до шва, не должна теряться из-за того, что мы сменили место её хранения.
        /// </summary>
        public void SortByCurrentOrder()
        {
            _parts.RemoveAll(p => p == null);

            // Сортировка СТАБИЛЬНАЯ (тайбрейк — прежний индекс): у парных конечностей ордер одинаковый, и
            // обычный Sort перетасовал бы левую с правой на ровном месте.
            var indexed = new List<(SpriteRenderer part, int index)>(_parts.Count);
            for (int i = 0; i < _parts.Count; i++) indexed.Add((_parts[i], i));
            indexed.Sort((a, b) =>
            {
                int byOrder = b.part.sortingOrder.CompareTo(a.part.sortingOrder);
                return byOrder != 0 ? byOrder : a.index.CompareTo(b.index);
            });

            _parts.Clear();
            for (int i = 0; i < indexed.Count; i++) _parts.Add(indexed[i].part);
            CachePartTransforms();
        }

        /// <summary>
        /// Разложить порядок списка в <c>sortingOrder</c> частей: верхний элемент получает наибольший ордер
        /// (рисуется поверх). Слой сортировки выравнивается по группе — часть с чужим слоем иначе выпадает
        /// из тела целиком.
        /// </summary>
        public void ApplyOrder()
        {
            int layer = SortingLayerId;
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;
                part.sortingOrder   = _parts.Count - 1 - i;
                part.sortingLayerID = layer;
            }
        }
    }
}
