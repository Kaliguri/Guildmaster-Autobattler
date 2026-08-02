namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Вид мероприятия — то, чем игрок сейчас занят. Называется при входе и за жизнь мероприятия не
    /// меняется: сменить вид можно только выйдя и войдя заново.
    /// </summary>
    /// <remarks>
    /// <b>Это факт, а не вывод.</b> Раньше «где мы» интерфейс выводил из двух косвенных признаков —
    /// есть ли забег и включена ли серая зона, — причём владелец второго жил внутри боевого скоупа.
    /// Пока бой был вечным, владелец всегда отвечал; когда бой стал рождаться по требованию, ответ
    /// пропал вместе с ним, и панель забега исчезла целиком (наход. Макса 02.08.2026).
    /// <para><b>PvP отдельным видом не заводится</b> (решение Макса 02.08.2026): это Ристалище с двумя
    /// ограничениями, и разница честно описывается двумя булями в <see cref="ActivitySetup"/>.</para>
    /// </remarks>
    public enum ActivityKind
    {
        /// <summary>Мероприятия нет: главное меню, бут, между режимами.</summary>
        None = 0,

        /// <summary>Забег по акту: карта, узлы, награды, экономика, сейв гильдии.</summary>
        Campaign = 1,

        /// <summary>Площадка вне забега: свободный состав, свободная расстановка, ни карты, ни сейва.</summary>
        ProvingGrounds = 2,
    }

    /// <summary>
    /// С чем открыто мероприятие: вид плюс ограничения площадки. Живёт в скоупе Занятия и читается
    /// всеми, кто внутри — включая бой.
    /// </summary>
    public readonly struct ActivitySetup
    {
        public readonly ActivityKind Kind;

        /// <summary>Состав противника скрыт до начала боя (PvP: подглядывать в чужой строй нельзя).</summary>
        public readonly bool HideOpponent;

        /// <summary>Расставлять можно только своих (PvP: чужой строй — не наше дело).</summary>
        public readonly bool OwnUnitsOnly;

        /// <summary>
        /// С каким составом открыта площадка; <c>null</c> — составом из ассета. Едет ПАРАМЕТРОМ входа, а
        /// не событием.
        /// </summary>
        /// <remarks>
        /// Событие тут не работает по устройству: расстановка, которая состав ставит, живёт в боевом
        /// скоупе и рождается ВМЕСТЕ с площадкой. Заказ, опубликованный до входа, слушать некому, а
        /// опубликованный после — приходит раньше, чем энтрипоинты успели подписаться (VContainer
        /// диспатчит <c>IStartable</c> отдельной фазой, а не синхронно). Параметр входа не зависит от
        /// момента вовсе. Событие осталось для смены состава на УЖЕ открытой площадке — там слушатель
        /// заведомо есть.
        /// </remarks>
        public readonly ProvingGroundsSetupRequest? Roster;

        public ActivitySetup(ActivityKind kind, bool hideOpponent = false, bool ownUnitsOnly = false,
            ProvingGroundsSetupRequest? roster = null)
        {
            Kind         = kind;
            HideOpponent = hideOpponent;
            OwnUnitsOnly = ownUnitsOnly;
            Roster       = roster;
        }

        /// <summary>Площадка с заказанным составом: дев-срез, а позже — сборка боя игроком.</summary>
        public static ActivitySetup GroundsWith(ProvingGroundsSetupRequest roster) =>
            new(ActivityKind.ProvingGrounds, roster: roster);

        /// <summary>Идёт ли мероприятие вообще.</summary>
        public bool IsOpen => Kind != ActivityKind.None;

        /// <summary>Забег по акту.</summary>
        public static ActivitySetup Campaign => new(ActivityKind.Campaign);

        /// <summary>Открытая площадка: оба состава видны, расставлять можно кого угодно.</summary>
        public static ActivitySetup ProvingGrounds => new(ActivityKind.ProvingGrounds);

        /// <summary>Матч: та же площадка, но чужой строй скрыт и неприкосновенен.</summary>
        public static ActivitySetup Pvp =>
            new(ActivityKind.ProvingGrounds, hideOpponent: true, ownUnitsOnly: true);
    }

    /// <summary>
    /// Мероприятие открылось или закрылось. Событие, а не только опрос: показу нужно знать МОМЕНТ, а
    /// не факт, — место являют и хоронят один раз, а не каждый кадр.
    /// </summary>
    /// <remarks>
    /// Закрытие мероприятия — это конец МЕСТА. Всё, что показ помнит про арену («уже собрана», «место
    /// сменилось»), обязано умереть вместе с ним, иначе второй заход на ту же площадку пройдёт молча:
    /// показ будет уверен, что всё это игрок уже видел (наход. Макса 02.08.2026).
    /// </remarks>
    public readonly struct ActivityChangedEvent
    {
        public readonly ActivitySetup Setup;

        public ActivityChangedEvent(ActivitySetup setup) => Setup = setup;

        /// <summary>Мероприятие идёт (иначе — только что кончилось).</summary>
        public bool IsOpen => Setup.IsOpen;
    }

    /// <summary>
    /// Чтение «какое мероприятие идёт» для тех, кто мероприятия переживает: верхняя панель, навигатор,
    /// звук. Живёт в Data по той же причине, что <see cref="IBattleClock"/>: <c>Guildmaster.UI</c> не
    /// ссылается на <c>Guildmaster.Game</c> — обратная ссылка дала бы цикл.
    /// </summary>
    public interface IActivityView
    {
        /// <summary>С чем открыто текущее мероприятие; вне мероприятия — <see cref="ActivityKind.None"/>.</summary>
        ActivitySetup Current { get; }
    }
}
