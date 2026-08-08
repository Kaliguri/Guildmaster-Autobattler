using System.Text;
using Guildmaster.Core.DevConsole;
using Guildmaster.Core.Players;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Дев-команды состава сеанса: посмотреть, кто в сеансе, и пересадить участника на другую сторону.
    /// </summary>
    /// <remarks>
    /// <b>Сторона — назначение, а не свойство режима</b> (решение Макса 08.08.2026). Пока менять её
    /// было нечем, «за кого я играю» задавалось конфигом и выводилось из вида мероприятия; проверить
    /// раскладку иначе как перезапуском с другим ассетом было нельзя. Эти команды — первый и пока
    /// единственный вход в новое назначение: кнопки игроку ещё нет, она появится вместе с лобби.
    /// <para><b>Живут в корневом скоупе</b>, как команды карты: состав переживает мероприятия и бои,
    /// а боевой скоуп рождается и умирает под ним.</para>
    /// </remarks>
    public static class SessionDevCommands
    {
        /// <summary>Положить команды состава в набор модуля (снимаются вместе с ним).</summary>
        public static void Register(DevCommandSet set)
        {
            if (set == null) return;

            set.Add("session", "Кто в сеансе: номер, имя, сторона, цвет, место", _ => Describe());

            set.Add("seat", "Посадить участника на сторону: seat <номер> <сторона>",
                a => Seat(a.GetInt(0), a.GetInt(1)),
                new DevParam("playerId", DevParamType.Int),
                new DevParam("team", DevParamType.Int));
        }

        public static string Describe()
        {
            ISessionRoster roster = Roster();
            if (roster == null) return "Сеанс не поднят — состава нет.";

            if (roster.Players.Count == 0)
                return $"В составе никого (наш номер {roster.LocalId}). Если сеанс идёт — это поломка " +
                       "разводки, а не пустая комната.";

            var text = new StringBuilder($"Участников {roster.Players.Count}, наш номер {roster.LocalId}:");
            foreach (SessionPlayer player in roster.Players)
            {
                text.AppendLine();
                text.Append(player.Id == roster.LocalId ? "  > " : "    ");
                text.Append(player);
            }

            return text.ToString();
        }

        public static string Seat(int playerId, int team)
        {
            ISessionRoster roster = Roster();
            if (roster == null) return "Сеанс не поднят — сажать некого.";

            if (!roster.TryGet(playerId, out SessionPlayer was))
                return $"Участника {playerId} в сеансе нет. Кто есть — команда «session».";

            roster.Seat(playerId, team);

            if (!roster.TryGet(playerId, out SessionPlayer now))
                return $"Участник {playerId} пропал из состава сразу после посадки — это поломка.";

            // Гость посадить не может: состав ведёт хозяин, и молчаливое «ничего не произошло» тут
            // читалось бы как сломанная команда.
            return now.Team == team
                ? $"{now.Name}: сторона {was.Team} → {now.Team}."
                : $"Сторона не изменилась ({now.Team}). Состав ведёт хозяин — у гостя посадка не его дело.";
        }

        private static ISessionRoster Roster()
        {
            var root = LifetimeScope.Find<Guildmaster.Game.RootLifetimeScope>();
            if (root == null || root.Container == null) return null;

            // Тот же приём, что в MapDevCommands: обобщённый Resolve у VContainer требует ключ,
            // а промах регистрации здесь не ошибка — просто сеанса ещё нет.
            try { return root.Container.Resolve(typeof(ISessionRoster)) as ISessionRoster; }
            catch { return null; }
        }
    }
}
