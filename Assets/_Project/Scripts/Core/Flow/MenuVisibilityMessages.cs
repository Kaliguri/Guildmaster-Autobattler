namespace Guildmaster.Core.Flow
{
    /// <summary>
    /// СОСТОЯНИЕ главного меню: открыто оно или нет. Вещает <c>MenuRouter</c> (владелец показа), слушает
    /// презентационный слой — за меню подкладывается тот же стол, что под картой акта, вместо пустоты,
    /// которую камера иначе заливает цветом очистки.
    /// </summary>
    /// <remarks>
    /// Лежит в <c>Core</c>, а не рядом с остальными сообщениями меню в <c>Guild</c>, намеренно: это шов
    /// между UI и презентацией, а <c>Presentation</c> на сборку <c>Guild</c> не ссылается и ссылаться не
    /// должна — слой отрисовки ничего не знает про забег.
    /// </remarks>
    public readonly struct MainMenuVisibilityChangedEvent
    {
        /// <summary>true — главное меню на экране, false — снято.</summary>
        public readonly bool Visible;

        public MainMenuVisibilityChangedEvent(bool visible) => Visible = visible;
    }

    /// <summary>
    /// СОСТОЯНИЕ заднего фона экранов: нужен ли под текущим экраном стол. Вещает UI-слой (он один знает,
    /// что сейчас на экране), слушает презентация (она одна умеет его нарисовать).
    /// </summary>
    /// <remarks>
    /// Заведено по QA #50: фон под ивентом был отдельной непрозрачной заливкой UI-цветом, и рядом с меню,
    /// где лежит настоящий стол, это читалось как «чёрный экран». Источник правды у фона теперь ОДИН —
    /// материал стола из <c>MapStyle</c>; UI лишь говорит, когда он нужен.
    /// </remarks>
    public readonly struct ScreenBackdropChangedEvent
    {
        /// <summary>true — под экраном нужен стол, false — за экраном живой мир (бой, карта, передышка).</summary>
        public readonly bool Visible;

        /// <summary>
        /// Стол нужен ДАЖЕ ЕСЛИ за экраном идёт живой бой. Обычно бой стол снимает: меню открывают, стоя
        /// посреди арены, и подложка закрыла бы ровно то, ради чего бой заведён. Но экран, который занял
        /// кадр целиком и панели не имеет (настройки), смотреть под собой не предлагает — там наоборот,
        /// мельтешение арены мешает читать строки.
        /// </summary>
        public readonly bool OverBattle;

        public ScreenBackdropChangedEvent(bool visible, bool overBattle = false)
        {
            Visible = visible;
            OverBattle = overBattle;
        }
    }

    /// <summary>
    /// Шторка перехода: насколько кадр закрыт (0 — открыт, 1 — закрыт наглухо). Вещает тот, кто ведёт
    /// переход (карта акта при выборе узла), рисует UI-слой поверх ВСЕГО.
    /// </summary>
    /// <remarks>
    /// Заведено по QA #47: шторка была мировым квадом и гасила только карту — топбар и панели оставались
    /// светлыми, и переход читался как «потемнело окошко», а не как смена сцены. Затемнить весь экран может
    /// только UI Toolkit: его панель рисуется поверх любых камер.
    /// </remarks>
    public readonly struct ScreenFadeChangedEvent
    {
        /// <summary>Плотность шторки, 0..1.</summary>
        public readonly float Progress;

        /// <summary>
        /// Точка, К КОТОРОЙ схлопывается кадр, в долях экрана (0..1, начало отсчёта — левый нижний угол).
        /// Центр экрана (0.5, 0.5) = обычное затемнение; точка узла = «ныряем именно туда».
        /// </summary>
        public readonly UnityEngine.Vector2 Center;

        /// <summary>
        /// Жребий ОДНОГО моргания: сдвиг выборки узора (xy), поворот в радианах (z) и множитель масштаба (w).
        /// Держится постоянным весь переход и меняется от перехода к переходу — одна текстура чернил даёт
        /// каждый раз новый рисунок, и повтора не видно.
        /// </summary>
        public readonly UnityEngine.Vector4 Seed;

        public ScreenFadeChangedEvent(float progress, UnityEngine.Vector2 center, UnityEngine.Vector4 seed)
        {
            Progress = progress;
            Center   = center;
            Seed     = seed;
        }
    }
}
