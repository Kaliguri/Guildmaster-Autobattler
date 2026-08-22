namespace Guildmaster.Core.Flow
{
    /// <summary>Тон титра: чем момент отличается на вид и на слух.</summary>
    public enum TitleRevealTone
    {
        /// <summary>Зов: «В бой!».</summary>
        Call,

        /// <summary>Победа.</summary>
        Triumph,

        /// <summary>Поражение.</summary>
        Defeat,
    }

    /// <summary>
    /// Показать титр: знак и крупное слово, которые въезжают в кадр и уходят сами.
    /// </summary>
    /// <remarks>
    /// <b>Один запрос на все четыре случая</b> (вердикт Макса 22.08.2026, пункт 7А плана
    /// [[ui-uplift]]): вход в бой, победа в бою, победа в забеге, поражение. Разница между ними —
    /// поля этой структуры, а не четыре похожих показа: «единый стиль и источник и переюзание под
    /// всякие ситуации».
    /// <para><b>Титр ничего не решает и никого не ждёт.</b> Ответа у него нет, кнопок нет, ввод он
    /// не ловит: показался и ушёл. Всё, что требует ответа, — это <see cref="NoticeRequest"/>.</para>
    /// <para><b>Ключ, а не готовая строка:</b> титр показывается и у гостя, а переводит каждый у
    /// себя.</para>
    /// </remarks>
    public readonly struct TitleRevealRequest
    {
        /// <summary>Ключ крупной строки.</summary>
        public readonly string LineKey;

        /// <summary>RU-литерал строки, пока ключа нет в таблице.</summary>
        public readonly string LineFallback;

        /// <summary>Ключ приписки под строкой. Пусто — приписки не будет.</summary>
        public readonly string SubKey;

        /// <summary>RU-литерал приписки.</summary>
        public readonly string SubFallback;

        /// <summary>Тон: зов, победа или поражение.</summary>
        public readonly TitleRevealTone Tone;

        /// <summary>
        /// Сколько титр держится в кадре, секунд (въезд и уход сверх этого). Ноль — берётся
        /// умолчание показа.
        /// </summary>
        /// <remarks>
        /// Время приходит от заказчика, потому что у случаев оно разное: зов в бой обязан уйти до
        /// первого удара, а победа забега держится, пока игрок смотрит.
        /// </remarks>
        public readonly float HoldSeconds;

        /// <summary>
        /// Имя знака над строкой — id из каталога знаков (<c>emblem.*</c>). Пусто — титр идёт без
        /// знака.
        /// </summary>
        /// <remarks>
        /// Id, а не текстура: слой потока не должен держать в руках ассеты — их разрешает показ, и
        /// подмена набора не заставит переписывать заказчиков.
        /// </remarks>
        public readonly string GlyphId;

        public TitleRevealRequest(string lineKey, string lineFallback, TitleRevealTone tone,
                                  float holdSeconds = 0f, string glyphId = null,
                                  string subKey = null, string subFallback = null)
        {
            LineKey      = lineKey;
            LineFallback = lineFallback;
            Tone         = tone;
            HoldSeconds  = holdSeconds;
            GlyphId      = glyphId;
            SubKey       = subKey;
            SubFallback  = subFallback;
        }

        /// <summary>Зов в бой — «В БОЙ!» со скрещёнными мечами.</summary>
        public static TitleRevealRequest ToBattle() =>
            new("ui.title.to_battle", "В бой!", TitleRevealTone.Call,
                holdSeconds: 1.1f, glyphId: "emblem.crossed-swords");

        /// <summary>Победа в бою — короткая, игрок ещё смотрит на поле.</summary>
        public static TitleRevealRequest BattleWon() =>
            new("ui.title.battle_won", "Победа", TitleRevealTone.Triumph,
                holdSeconds: 1.3f, glyphId: "emblem.crown");

        /// <summary>Победа в забеге — своя: держится дольше и говорит, что кончился акт, а не бой.</summary>
        public static TitleRevealRequest RunWon() =>
            new("ui.title.run_won", "Акт пройден", TitleRevealTone.Triumph,
                holdSeconds: 2.2f, glyphId: "emblem.crown",
                subKey: "ui.title.run_won.sub", subFallback: "Гильдия возвращается с победой");

        /// <summary>Поражение — общее для боя и забега: разницу говорит приписка, а не второй титр.</summary>
        public static TitleRevealRequest Defeat(bool runOver) =>
            new("ui.title.defeat", "Поражение", TitleRevealTone.Defeat,
                holdSeconds: runOver ? 2.2f : 1.3f, glyphId: "emblem.skull-crossed-bones",
                subKey: runOver ? "ui.title.defeat.run" : null,
                subFallback: runOver ? "Забег окончен" : null);
    }
}
