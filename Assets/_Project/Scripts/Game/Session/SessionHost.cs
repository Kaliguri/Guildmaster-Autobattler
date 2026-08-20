using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Владелец жизненного цикла Сессии — сеанса владения состоянием игры. Открывает её на старте
    /// (соло-игрок всегда владелец своего сейва) и переоткрывает, когда владелец меняется: вход в чужой
    /// кооп-сеанс гостем, смена профиля или гильдии. Он же — дорога к содержимому сессии для тех, кто
    /// живёт дольше неё.
    /// </summary>
    /// <remarks>
    /// <b>Живёт в корне, а рождает от мира</b> — тем же приёмом, что <see cref="Activity.ActivityHost"/>:
    /// мир грузится аддитивно после корня, инъекцией его не взять, зато иерархия выходит задуманной
    /// (Мир → Сессия → Занятие → Бой).
    /// <para><b>Смена владельца — это смерть сессии, а не пересборка состояния.</b> Пока состояние
    /// забега жило в корне вечным объектом, «начать сначала» приходилось объявлять вручную, и любая
    /// забытая строка тянула прошлую гильдию в следующий сеанс. Теперь новый владелец — новый скоуп, а
    /// старое состояние уходит вместе с прежним.</para>
    /// <para><b>Наружу отдаём узкие фасады, а не контейнер:</b> кто угодно с резолвером дотянулся бы до
    /// чего угодно в чужой жизни и получил ссылку, протухающую вместе с сеансом.</para>
    /// </remarks>
    public sealed class SessionHost : IDisposable
    {
        private LifetimeScope _session;

        /// <summary>Идёт ли сеанс прямо сейчас.</summary>
        public bool IsOpen => _session != null && _session.Container != null;

        /// <summary>Кем играем; вне сессии — <c>null</c>.</summary>
        public SessionContext Context => Resolve<SessionContext>();

        /// <summary>
        /// Держатель забега текущего сеанса. <c>null</c> вне сессии И у гостя: у него состояние не
        /// своё, писать сейв ему нечем — см. <see cref="SessionInstaller"/>.
        /// </summary>
        public Guildmaster.Guild.RunStateService Run => Resolve<Guildmaster.Guild.RunStateService>();

        /// <summary>
        /// Что в забеге сейчас — у обеих ролей. У владельца отвечает держатель, у гостя — приёмник
        /// снимков; спрашивающему разница не видна.
        /// </summary>
        public Guildmaster.Guild.ISessionRunState RunView => Resolve<Guildmaster.Guild.ISessionRunState>();

        /// <summary>
        /// Кто в сеансе и за какую сторону играет; вне сессии — <c>null</c>. У владельца состав ведётся,
        /// у гостя принимается — спрашивающему разница не видна.
        /// </summary>
        public Guildmaster.Core.Players.ISessionRoster Roster
            => Resolve<Guildmaster.Core.Players.ISessionRoster>();

        /// <summary>Чужие курсоры текущего сеанса; вне сессии — <c>null</c>, рисовать нечего.</summary>
        public Guildmaster.Core.Players.IPresenceView Presence
            => Resolve<Guildmaster.Core.Players.IPresenceView>();

        /// <summary>
        /// Общее согласие текущего сеанса; вне сессии — <c>null</c>. У владельца счёт ведётся, у гостя
        /// голос отправляется — спрашивающему разница не видна.
        /// </summary>
        public Guildmaster.Core.Net.ISharedDecision Decision
            => Resolve<Guildmaster.Core.Net.ISharedDecision>();

        /// <summary>
        /// Объявление экранов узла; <c>null</c> вне сессии И у гостя — объявляет тот, кто ведёт узел.
        /// </summary>
        public Net.HostSessionStage SessionStage => Resolve<Net.HostSessionStage>();

        /// <summary>
        /// Команды забега текущего сеанса; вне сессии — <c>null</c>. У владельца это локальная шина, у
        /// гостя — отправка интента хосту; спрашивающему разница не видна и не нужна.
        /// </summary>
        /// <remarks>
        /// Спрашиваем <c>ISessionRunCommands</c>, а не <c>IRunCommands</c>: последний в корне занят
        /// роутером, который сам ходит сюда, — резолв ушёл бы вверх по контейнеру и вернулся к роутеру же.
        /// </remarks>
        public Guildmaster.Guild.Commands.ISessionRunCommands Commands
            => Resolve<Guildmaster.Guild.Commands.ISessionRunCommands>();

        /// <summary>
        /// Открыть сеанс с указанной ролью. Прошлый закрывается: два владельца состояния одновременно —
        /// это и есть та самая рассинхронизация, ради запрета которой уровень заведён.
        /// </summary>
        public void Open(SessionRole role)
        {
            Close();

            var world = LifetimeScope.Find<WorldLifetimeScope>();
            if (world == null || world.Container == null)
            {
                Debug.LogError("[SessionHost] - мир ещё не поднят → сеанс открывать не от кого. " +
                               "Сцены грузятся раньше игры (см. GameBootstrap).");
                return;
            }

            _session = world.CreateChild(new SessionInstaller(role), $"[Session] {role}");
        }

        /// <summary>
        /// Закрыть сеанс: забег, его сейв-владелец и лог команд уходят вместе с ним. Мероприятие, если
        /// оно шло, умирает следом — оно рождено внутри этой жизни.
        /// </summary>
        public void Close()
        {
            if (_session == null) return;

            _session.Dispose();
            _session = null;
        }

        public void Dispose() => Close();

        /// <summary>
        /// Родить дочерний скоуп ВНУТРИ жизни сеанса. Так мероприятие получает доступ к состоянию
        /// забега и к роли, не получая права пережить сеанс, в котором началось.
        /// </summary>
        /// <remarks>
        /// Наружу отдаётся сам дочерний скоуп, а не контейнер сессии: заказчик владеет тем, что создал,
        /// и ничем больше. Сессии нет — <c>null</c>, и это законный ответ: мероприятию не от кого
        /// рождаться.
        /// </remarks>
        public LifetimeScope CreateChild(IInstaller installer, string name)
        {
            if (!IsOpen)
            {
                Debug.LogError("[SessionHost] - сеанс не открыт → дочернему скоупу рождаться не от кого. " +
                               "Сессию открывает GameBootstrap сразу после подъёма мира.");
                return null;
            }
            return _session.CreateChild(installer, name);
        }

        private T Resolve<T>() where T : class
        {
            if (!IsOpen) return null;
            return _session.Container.TryResolve(out T value) ? value : null;
        }
    }
}
