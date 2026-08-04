using System;
using System.Text;
using Guildmaster.Core.DevConsole;
using Guildmaster.Core.Diagnostics;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Команды диагностического лога: включить каналы прямо в игре и найти, куда сложены прогоны.
    /// </summary>
    /// <remarks>
    /// <b>Через консоль, а не через поле в исходнике.</b> Кооп ломается на двух машинах и у живого
    /// Steam; чтобы посмотреть прогон, прежде нужно было пересобрать билд с поднятым флагом и заново
    /// позвать второго человека. Теперь канал включается в той сборке, которая уже стоит у обоих
    /// (просьба Макса 04.08.2026).
    /// </remarks>
    public static class DiagCommands
    {
        public static void Register(DevCommandSet set)
        {
            set.Add("diag", "Диагностический лог: diag (показать) · diag net on · diag tape off · diag none",
                args => Handle(args),
                new DevParam("channel", DevParamType.String, optional: true),
                new DevParam("state", DevParamType.String, optional: true));

            set.Add("diaglogs", "Где лежат логи прошлых прогонов (их можно прислать целиком)",
                _ => SessionLogArchive.Folder);
        }

        private static string Handle(DevArgs args)
        {
            string raw = args?.Raw(0);
            if (string.IsNullOrEmpty(raw)) return Describe();

            string channelName = raw.Trim().ToLowerInvariant();

            if (channelName == "none")
            {
                Diag.Restore(DiagChannel.None);
                return "Диагностика выключена целиком.";
            }

            if (!TryParse(channelName, out DiagChannel channel))
                return $"Не знаю канала «{channelName}». Есть: net, session, tape, follow, commands, ready, none.";

            string state = args.Raw(1);
            bool on = string.IsNullOrEmpty(state)
                      || state.Trim().ToLowerInvariant() is "on" or "1" or "true";

            Diag.Set(channel, on);
            return Describe();
        }

        private static bool TryParse(string name, out DiagChannel channel)
        {
            switch (name)
            {
                case "net":      channel = DiagChannel.Net;      return true;
                case "session":  channel = DiagChannel.Session;  return true;
                case "tape":     channel = DiagChannel.Tape;     return true;
                case "follow":   channel = DiagChannel.Follow;   return true;
                case "commands": channel = DiagChannel.Commands; return true;
                case "ready":    channel = DiagChannel.Ready;    return true;
                default:         channel = DiagChannel.None;     return false;
            }
        }

        private static string Describe()
        {
            if (Diag.Enabled == DiagChannel.None) return "Диагностика выключена. Включить: diag net on";

            var text = new StringBuilder("Пишут каналы: ");
            text.Append(Diag.Enabled);
            text.Append(". Логи прогонов: ").Append(SessionLogArchive.Folder);
            return text.ToString();
        }
    }
}
