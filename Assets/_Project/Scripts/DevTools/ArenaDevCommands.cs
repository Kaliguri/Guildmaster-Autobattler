using System.Text;
using Guildmaster.Core.Arena;
using Guildmaster.Presentation.Arena;
using QFSW.QC;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Дев-команды смены облика арены: гонять переход «А → Б» туда-сюда, не проходя забег.
    /// Крутить форму (длительность, разброс, темп) — в инспекторе <see cref="ArenaSkinSwapper"/>,
    /// правки подхватываются на следующем запуске перехода.
    /// </summary>
    public static class ArenaDevCommands
    {
        [Command("gm_arena_skins", "Список обликов арены (id) и какой надет сейчас")]
        public static string Skins()
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден (мир не поднят?).";

            var sb = new StringBuilder();
            sb.AppendLine($"Сейчас: {swapper.CurrentSkinId}");
            foreach (ArenaSkinSource src in Object.FindObjectsByType<ArenaSkinSource>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  {src.SkinId}{(src.IsLive ? "  (живой корень)" : "")}");
            return sb.ToString();
        }

        [Command("gm_arena_swap", "Сменить облик арены с анимацией: gm_arena_swap <skinId>")]
        public static string Swap(string skinId)
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден (мир не поднят?).";
            if (swapper.Busy) return "Переход уже идёт — дождись или gm_arena_rush.";

            return swapper.Play(skinId)
                ? $"Переход на '{skinId}' пошёл ({swapper.Shape.DurationSeconds:0.0}с)."
                : $"Не выйдет: облика '{skinId}' нет либо он уже надет.";
        }

        [Command("gm_arena_rush", "Резко ускорить идущий переход (то же, что Space)")]
        public static string Rush()
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден.";
            if (!swapper.Busy) return "Переход не идёт.";

            swapper.Rush();
            return "Ускорено.";
        }

        [Command("gm_arena_set", "Надеть облик мгновенно, без анимации: gm_arena_set <skinId>")]
        public static string Set(string skinId)
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден.";

            swapper.ApplyInstant(skinId);
            return $"Надет '{swapper.CurrentSkinId}'.";
        }

        // Свопер живёт в мировом (persist) скоупе — переход обязан доигрывать и когда бой уже кончился.
        private static ArenaSkinSwapper Swapper()
        {
            foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>(FindObjectsSortMode.None))
            {
                if (scope.GetType().Name != "WorldLifetimeScope" || scope.Container == null) continue;
                try { return scope.Container.Resolve(typeof(IArenaSwap)) as ArenaSkinSwapper; }
                catch { /* не зарегистрирован — упадём на поиск в сцене ниже */ }
            }
            return Object.FindFirstObjectByType<ArenaSkinSwapper>();
        }
    }
}
