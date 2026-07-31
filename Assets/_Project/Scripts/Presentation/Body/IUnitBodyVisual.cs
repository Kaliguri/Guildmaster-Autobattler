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

        public BodyVisualState(Color tint, float flash, Color flashColor,
            float holo, Color holoColor, float holoAlpha, float holoScanScale, float holoScanAmount,
            float outline, Color outlineColor)
        {
            Tint = tint;
            Flash = flash; FlashColor = flashColor;
            Holo = holo; HoloColor = holoColor; HoloAlpha = holoAlpha;
            HoloScanScale = holoScanScale; HoloScanAmount = holoScanAmount;
            Outline = outline; OutlineColor = outlineColor;
        }

        /// <summary>Нужен ли вообще property block: всё по нулям — рендерер идёт обычным путём.</summary>
        public bool HasEffect => Flash > 0.0001f || Holo > 0.0001f || Outline > 0.0001f;

        public bool Equals(BodyVisualState other) =>
            Tint == other.Tint &&
            Mathf.Approximately(Flash, other.Flash) && FlashColor == other.FlashColor &&
            Mathf.Approximately(Holo, other.Holo) && HoloColor == other.HoloColor &&
            Mathf.Approximately(HoloAlpha, other.HoloAlpha) &&
            Mathf.Approximately(HoloScanScale, other.HoloScanScale) &&
            Mathf.Approximately(HoloScanAmount, other.HoloScanAmount) &&
            Mathf.Approximately(Outline, other.Outline) && OutlineColor == other.OutlineColor;

        public override bool Equals(object obj) => obj is BodyVisualState s && Equals(s);
        public override int GetHashCode() => System.HashCode.Combine(Tint, Flash, FlashColor, Holo, Outline, OutlineColor);
    }

    /// <summary>
    /// «Тело юнита» глазами презентации: то, что можно покрасить, засветить, отсортировать, отзеркалить,
    /// спрятать и разбить на осколки. Шов существует ради того, что у покадрового юнита тело — ОДИН
    /// спрайт, а у скелетного — полтора десятка частей, и всё, что раньше писалось в один
    /// <see cref="SpriteRenderer"/>, на скелете попадало только в торс: вспыхивала грудь, разлетался
    /// торс, Y-сортировка вырывала его из собственного тела.
    /// <para>
    /// Реализации живут ОДНОВРЕМЕННО и выбираются полем на префабе вида: пусто — <see cref="SpriteBodyVisual"/>
    /// поверх существующего <c>_sprite</c> (17 покадровых префабов не трогаются вовсе), задан
    /// <see cref="SkeletalBodyVisual"/> — он и есть тело.
    /// </para>
    /// </summary>
    public interface IUnitBodyVisual
    {
        /// <summary>Есть ли что показывать (спрайт назначен / части найдены). false — тело рисовать нечем.</summary>
        bool HasContent { get; }

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
