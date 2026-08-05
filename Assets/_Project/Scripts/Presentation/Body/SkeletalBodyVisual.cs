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
        private UnitPartRegistry      _registry;         // кто из частей оружие, щит, кисть, голова — выведено из конвенции рига
        private bool                  _groupWarned;      // про отсутствие группы говорим один раз, а не каждый кадр

        /// <summary>Рендереры частей в порядке отрисовки (только чтение — владелец порядка это компонент).</summary>
        public IReadOnlyList<SpriteRenderer> Renderers => _parts;

        /// <summary>Части тела для адресных запросов — реестр по конвенции рига.</summary>
        public IUnitPartLookup Parts => _registry ??= BuildRegistry();

        private PartMask _tintMask;
        private bool     _tintMaskBuilt;

        /// <summary>
        /// Какие части принимают тинт юнита: волосы и всё, что держится в руках, — любое оружие и щит.
        /// </summary>
        /// <remarks>
        /// Считается один раз на тело и живёт до пересборки списка: состав частей внутри боя не меняется,
        /// а <see cref="Apply"/> зовётся каждый кадр на каждого бойца.
        /// <para>Ролей у частей две, и обе уже есть в реестре: предмет опознаётся хватом
        /// (<see cref="UnitPart.IsHeld"/>), волосы — именем узла рисунка (<see cref="RigNaming.IsHair"/>),
        /// потому что своей кости у них нет и быть не должно.</para>
        /// </remarks>
        private PartMask TintMask()
        {
            if (_tintMaskBuilt) return _tintMask;

            PartMask mask = PartMask.Empty;
            IReadOnlyList<UnitPart> parts = Parts.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart part = parts[i];
                bool paints = part.IsHeld || RigNaming.IsHair(part.Renderer != null ? part.Renderer.name : null);
                if (paints) mask |= PartMask.Single(part.Index);
            }

            _tintMask      = mask;
            _tintMaskBuilt = true;
            return _tintMask;
        }

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
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                _partTransforms[i] = part != null ? part.transform : null;
            }
            _registry      = null;   // состав частей сменился — анатомию пересобираем при первом запросе
            _tintMaskBuilt = false;  // вместе с анатомией пересчитывается и «кого красим»
            CacheAuthoredColors();
        }

        /// <summary>
        /// Авторские цвета частей — те, что стоят в префабе. Некрашеная часть возвращается ИМЕННО К НИМ,
        /// а не к белому: белый в <c>SpriteRenderer.color</c> означает «спрайт как есть», а художник
        /// красит vertex-цветом прямо в риге — лицо телесным, тело холодно-серым, волосы тёмно-красным.
        /// Подставив белый, мы стирали эту работу и получали белое лицо (найдено Максом 05.08.2026).
        /// </summary>
        /// <remarks>
        /// Снимок берётся вместе с составом частей, то есть ДО первой покраски: вид переиспользуется из
        /// пула целиком, и снимать цвета в <c>Apply</c> значило бы запомнить уже перекрашенное.
        /// </remarks>
        private void CacheAuthoredColors()
        {
            _authoredColors = new Color[_parts.Count];
            for (int i = 0; i < _parts.Count; i++)
                _authoredColors[i] = _parts[i] != null ? _parts[i].color : Color.white;
        }

        private Color[] _authoredColors;

        /// <summary>
        /// Реестр частей строится ЛЕНИВО, а не в <c>Awake</c>: стенд анимаций и валидатор рига поднимают тело
        /// без всякого боя, а обход иерархии с чтением меток им не нужен. Первый запрос делает это один раз.
        /// </summary>
        private UnitPartRegistry BuildRegistry() => UnitPartRegistry.FromBody(_parts, transform, this);

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
            // ТИНТ КРАСИТ НЕ ВСЁ ТЕЛО, А ВОЛОСЫ И ТО, ЧТО В РУКАХ (решение Макса 05.08.2026: «Скорее
            // только волосы И любое оружие»). Остальное носит цвет, каким его нарисовали: кожа, лицо и
            // одежда, помноженные на оттенок юнита, уходят в грязь, а сталь клинка и прядь волос
            // принимают цвет честно — они и написаны под покраску.
            //
            // Альфа идёт ВСЕМ: она несёт прозрачность инвиза, и некрашеная часть обязана исчезать
            // вместе с телом, иначе стелс оставит на арене плавающий торс. Меняется только она —
            // сам цвет некрашеной части остаётся авторским (см. CacheAuthoredColors).
            PartMask tinted = TintMask();
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;

                if (tinted.Has(i)) { part.color = state.Tint; continue; }

                Color authored = _authoredColors != null && i < _authoredColors.Length
                    ? _authoredColors[i]
                    : Color.white;
                authored.a = state.Tint.a;
                part.color = authored;
            }

            bool active = state.HasEffect;
            if (!active && !_effectApplied) return;
            if (active && _effectApplied && state.Equals(_lastState)) return;

            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;
                // Маска адресует части ПО ИНДЕКСУ в этом списке — том же, что несёт UnitPart.Index.
                bool partGlows = state.GlowParts.Has(i);
                part.GetPropertyBlock(_mpb);
                BodyShaderIds.Write(_mpb, state, part.sprite, partGlows);
                part.SetPropertyBlock(_mpb);
            }

            _effectApplied = active;
            _lastState     = state;
        }

        // Буферы порезов переиспользуются: раскладка идёт по всем частям на каждое попадание, и новый
        // массив на часть означал бы полтора десятка аллокаций за удар.
        private Vector4[] _cutBuffer;
        private float[]   _cutGlow;
        private bool      _cutsWritten;   // на теле сейчас есть хоть один нарисованный порез

        public void ApplyCuts(IReadOnlyList<BodyCut> cuts, Color colour, float width)
        {
            bool any = cuts != null && cuts.Count > 0;
            if (!any && !_cutsWritten) return;   // порезов не было и нет — трогать шестнадцать блоков не за чем

            _mpb       ??= new MaterialPropertyBlock();
            _cutBuffer ??= new Vector4[BodyShaderIds.MaxCutsPerPart];
            _cutGlow   ??= new float[BodyShaderIds.MaxCutsPerPart];

            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null) continue;

                int count = 0;
                if (any)
                {
                    // Идём с КОНЦА: при переполнении части остаются свежие раны, а не первые попавшиеся.
                    for (int c = cuts.Count - 1; c >= 0 && count < BodyShaderIds.MaxCutsPerPart; c--)
                    {
                        BodyCut cut = cuts[c];
                        if (cut.PartIndex != i) continue;
                        float bright = cut.Brightness;
                        if (bright <= 1e-3f) continue;

                        _cutBuffer[count] = new Vector4(cut.Local.x, cut.Local.y, cut.Angle, cut.Length);
                        _cutGlow[count]   = bright;
                        count++;
                    }
                }

                // Ширина приходит в МИРОВЫХ единицах, а шейдер считает в локальных координатах части:
                // у частей внутри рига свой масштаб, и без перевода рана на мече была бы толще, чем на теле.
                float scale = part.transform.lossyScale.x;
                float localWidth = width / Mathf.Max(1e-5f, Mathf.Abs(scale));

                part.GetPropertyBlock(_mpb);
                BodyShaderIds.WriteCuts(_mpb, _cutBuffer, _cutGlow, count, colour, localWidth);
                part.SetPropertyBlock(_mpb);
            }

            _cutsWritten = any;
        }

        public bool TryBuildCut(Vector3 world, Vector2 worldDir, float worldLength, float budget, out BodyCut cut)
        {
            cut = default;

            int partIndex = -1;
            SpriteRenderer best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer part = _parts[i];
                if (part == null || part.sprite == null || !part.enabled) continue;

                // Расстояние до габарита части: ноль значит «точка внутри». Первая накрывшая побеждает —
                // список идёт сверху вниз, то есть от той части, которая рисуется поверх остальных.
                float d = part.bounds.SqrDistance(world);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = part;
                    partIndex = i;
                }
                if (d <= 0f) break;
            }

            if (best == null) return false;

            cut = BuildCutOn(best, partIndex, world, worldDir, worldLength, budget);
            return true;
        }

        /// <summary>
        /// Перевести мировой удар в порез на конкретной части. Место, направление и длина считаются
        /// трансформом самой части, поэтому зеркало тела и поворот кости учтены сами собой.
        /// </summary>
        internal static BodyCut BuildCutOn(Renderer renderer, int partIndex,
            Vector3 world, Vector2 worldDir, float worldLength, float budget)
        {
            Transform t = renderer.transform;

            Vector3 localPoint = t.InverseTransformPoint(world);
            Vector3 localDir   = t.InverseTransformDirection(new Vector3(worldDir.x, worldDir.y, 0f));

            float angle = localDir.sqrMagnitude > 1e-8f
                ? Mathf.Atan2(localDir.y, localDir.x)
                : 0f;

            // Во сколько локальных единиц части укладывается одна мировая: части сидят внутри рига со
            // своим масштабом, и длина, заданная в долях роста, обязана его учесть.
            float scale  = Mathf.Abs(t.lossyScale.x);
            float length = worldLength / Mathf.Max(1e-5f, scale);

            return new BodyCut(partIndex, new Vector2(localPoint.x, localPoint.y), angle, length, budget, budget);
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
            // Мера осколка — рост ВСЕГО тела, а не своей части: иначе кисть и клинок рассыпаются на
            // одинаковое ЧИСЛО кусков, то есть на куски совершенно разного размера.
            float height = TryGetBounds(out Bounds body) ? body.size.y : 1f;

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
                shatter.Play(part, feel, palette, height, () =>
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
