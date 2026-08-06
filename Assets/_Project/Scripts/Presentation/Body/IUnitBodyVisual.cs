using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Материальное состояние тела за кадр — всё, что презентация подмешивает поверх спрайта: тинт с
    /// альфой инвиза, вспышка удара, голограмма развоплощения, контур каста. Подаётся ОДНИМ куском,
    /// а не четырьмя вызовами: у скелетного тела получателей полтора десятка, и раскладывать по ним
    /// каждый параметр отдельно значило бы шестнадцать записей property block на каждый параметр.
    /// </summary>
    public readonly struct BodyVisualState : System.IEquatable<BodyVisualState>
    {
        /// <summary>Цвет тела (умножается на текстуру), альфа несёт прозрачность инвиза.</summary>
        public readonly Color Tint;
        /// <summary>0..1 — сила вспышки (<c>_FlashAmount</c>).</summary>
        public readonly float Flash;
        public readonly Color FlashColor;
        /// <summary>0..1 — сила голограммы (<c>_Holo</c>).</summary>
        public readonly float Holo;
        public readonly Color HoloColor;
        public readonly float HoloAlpha;
        public readonly float HoloScanScale;
        public readonly float HoloScanAmount;
        /// <summary>0..1 — сила контура каста (<c>_Outline</c>).</summary>
        public readonly float Outline;
        public readonly Color OutlineColor;

        /// <summary>0..1 — сила свечения части-источника (<c>_GlowAmount</c>); применяется только к частям в <see cref="GlowParts"/>.</summary>
        public readonly float Glow;
        /// <summary>HDR-цвет свечения (компоненты могут быть &gt;1): цвет юнита, поднятый под порог bloom.</summary>
        public readonly Color GlowColor;
        /// <summary>
        /// Какие ИМЕННО части сейчас светятся. Не роль («любое оружие»), а адрес: у бойца с двумя кинжалами
        /// приём может зажечь один из них, и роль такое не выражает.
        /// </summary>
        public readonly PartMask GlowParts;

        /// <summary>
        /// 0..1 — какая доля свечения ложится ровно, не считаясь с артом (<c>_GlowShapeKeep</c>). Ниже —
        /// свет идёт по яркости пикселя, и форма светящейся части остаётся читаемой.
        /// </summary>
        public readonly float GlowFlatness;

        public BodyVisualState(Color tint, float flash, Color flashColor,
            float holo, Color holoColor, float holoAlpha, float holoScanScale, float holoScanAmount,
            float outline, Color outlineColor,
            float glow, Color glowColor, PartMask glowParts, float glowFlatness)
        {
            Tint = tint;
            Flash = flash; FlashColor = flashColor;
            Holo = holo; HoloColor = holoColor; HoloAlpha = holoAlpha;
            HoloScanScale = holoScanScale; HoloScanAmount = holoScanAmount;
            Outline = outline; OutlineColor = outlineColor;
            Glow = glow; GlowColor = glowColor; GlowParts = glowParts; GlowFlatness = glowFlatness;
        }

        /// <summary>Светится ли хоть одна часть: сила выше нуля И маска не пуста.</summary>
        public bool HasGlow => Glow > 0.0001f && !GlowParts.IsEmpty;

        /// <summary>Нужен ли вообще property block: всё по нулям — рендерер идёт обычным путём.</summary>
        public bool HasEffect => Flash > 0.0001f || Holo > 0.0001f || Outline > 0.0001f || HasGlow;

        public bool Equals(BodyVisualState other) =>
            Tint == other.Tint &&
            Mathf.Approximately(Flash, other.Flash) && FlashColor == other.FlashColor &&
            Mathf.Approximately(Holo, other.Holo) && HoloColor == other.HoloColor &&
            Mathf.Approximately(HoloAlpha, other.HoloAlpha) &&
            Mathf.Approximately(HoloScanScale, other.HoloScanScale) &&
            Mathf.Approximately(HoloScanAmount, other.HoloScanAmount) &&
            Mathf.Approximately(Outline, other.Outline) && OutlineColor == other.OutlineColor &&
            Mathf.Approximately(Glow, other.Glow) && GlowColor == other.GlowColor &&
            GlowParts == other.GlowParts && Mathf.Approximately(GlowFlatness, other.GlowFlatness);

        public override bool Equals(object obj) => obj is BodyVisualState s && Equals(s);
        public override int GetHashCode() => System.HashCode.Combine(Tint, Flash, FlashColor, Holo, Outline, OutlineColor, Glow, GlowParts);
    }

    /// <summary>
    /// «Тело юнита» глазами презентации: то, что можно покрасить, засветить, отсортировать, отзеркалить,
    /// спрятать и разбить на осколки. Шов появился ради того, что у покадрового юнита тело было ОДНИМ
    /// спрайтом, а у скелетного — полтора десятка частей, и всё, что раньше писалось в один
    /// <see cref="SpriteRenderer"/>, на скелете попадало только в торс: вспыхивала грудь, разлетался
    /// торс, Y-сортировка вырывала его из собственного тела.
    /// <para>
    /// <b>Реализация осталась одна</b> — <see cref="SkeletalBodyVisual"/>: покадровый путь удалён
    /// 06.08.2026, последний такой префаб ушёл из дерева днём раньше. Интерфейс живёт дальше, потому что
    /// он и есть та граница, за которую презентация не заглядывает: обращение к <see cref="SpriteRenderer"/>
    /// в обход этого шва — признак поломки, а не оптимизации.
    /// </para>
    /// </summary>
    public interface IUnitBodyVisual
    {
        /// <summary>Есть ли что показывать (спрайт назначен / части найдены). false — тело рисовать нечем.</summary>
        bool HasContent { get; }

        /// <summary>
        /// Части тела для адресных запросов: что в руке, где голова, чем ударит. Тело — единственный, кто
        /// знает свою анатомию, поэтому реестр отдаёт оно, а не собирает каждый желающий по иерархии.
        /// </summary>
        IUnitPartLookup Parts { get; }

        /// <summary>
        /// Узел, в пространстве которого живёт арт тела: его масштабирует сплющивание, от него берётся
        /// разворот. Стоит ВЫШЕ <see cref="Animator"/>, иначе клип затирает масштаб каждым кадром.
        /// </summary>
        Transform Root { get; }

        /// <summary>
        /// Праймить per-instance путь: спрайт с нашим SRP-batcher-шейдером подхватывает
        /// <c>color</c>/<c>flip</c> только после первой записи property block, иначе юнит стоит без
        /// своего цвета до первого удара.
        /// </summary>
        void Prime(Color flashColor);

        /// <summary>Применить состояние кадра. Реализация сама решает, писать ли — повтор того же не пишется.</summary>
        void Apply(in BodyVisualState state);

        /// <summary>
        /// Переписать порезы на теле. Отдельный вызов, а не поле в <see cref="BodyVisualState"/>: состояние
        /// кадра подаётся каждый кадр и адресует тело ЦЕЛИКОМ, а порезы меняются редко (удар, лечение) и
        /// адресуются каждый своей части — раскладывать список по шестнадцати частям шестьдесят раз в
        /// секунду не за чем.
        /// </summary>
        /// <param name="cuts">Все раны тела в порядке нанесения. Пустой список стирает их с тела.</param>
        /// <param name="colour">Цвет вскрытого — приходит из палитры через feel-конфиг, а не из тела.</param>
        /// <param name="width">Полуширина светящейся линии, мировые единицы.</param>
        void ApplyCuts(System.Collections.Generic.IReadOnlyList<BodyCut> cuts, Color colour, float width);

        /// <summary>
        /// Собрать порез из мирового удара: найти часть, на которую он лёг, и перевести место, направление
        /// и длину в её координаты.
        /// </summary>
        /// <param name="world">Точка попадания в мире.</param>
        /// <param name="worldDir">Направление удара в мире — порез идёт вдоль него, как и форма.</param>
        /// <param name="worldLength">Длина пореза, мировые единицы.</param>
        /// <param name="budget">Сколько HP снял удар: запас, по которому порез заживает.</param>
        /// <param name="cut">Готовый порез в координатах своей части.</param>
        /// <returns><c>false</c> — вскрывать нечего: тела нет либо у него нет ни одной живой части.</returns>
        bool TryBuildCut(Vector3 world, Vector2 worldDir, float worldLength, float budget, out BodyCut cut);

        /// <summary>Порядок в слое сортировки. У скелета его получает группа целиком — см. <c>UnitView</c>.</summary>
        void SetSortingOrder(int order);

        /// <summary>Слой сортировки тела — по нему презентация ставит VFX относительно юнита.</summary>
        int SortingLayerId { get; }

        /// <summary>Текущий порядок сортировки тела.</summary>
        int SortingOrder { get; }

        /// <summary>
        /// Отзеркалить тело по X. Покадровое тело разворачивает сам спрайт, скелетное — знак масштаба
        /// <see cref="Root"/>: у составного тела <c>flipX</c> на каждой части отразил бы части по отдельности,
        /// не поменяв их взаимного расположения.
        /// </summary>
        void SetFlipX(bool flip);

        /// <summary>Отзеркалено ли тело сейчас.</summary>
        bool IsFlippedX { get; }

        /// <summary>Мировой AABB тела (у скелета — объединение частей). false — рисовать нечего.</summary>
        bool TryGetBounds(out Bounds bounds);

        /// <summary>Снять силуэт для drag-призрака расстановки: части с их позами относительно ног.</summary>
        UnitSilhouette CaptureSilhouette(Vector2 feet);

        /// <summary>
        /// Показать/скрыть тело. Скрывается на разлёте осколков, ВОЗВРАЩАЕТСЯ при новой привязке: виды
        /// переиспользуются после чьей-то смерти, и без явного возврата второй жилец такого вида приходил
        /// на арену невидимым.
        /// </summary>
        void SetVisible(bool visible);

        /// <summary>
        /// Разлёт на осколки. У скелета колется КАЖДАЯ часть — общей палитрой и общим таймингом:
        /// тело разлетается по частям, и это заодно честнее одного прямоугольника торса.
        /// </summary>
        /// <param name="onComplete">Зовётся один раз, когда догорел последний осколок.</param>
        void PlayShatter(Design.CombatFeelConfig feel, Gradient palette, System.Action onComplete);
    }
}
