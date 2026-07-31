using System.Linq;
using System.Text;
using Guildmaster.Presentation.Effects;
using Guildmaster.Core.DevConsole;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Дев-команды к общему реестру визуальных эффектов: посмотреть список, погасить, вернуть, сравнить
    /// «с эффектом и без» прямо в игре, не пересобирая сцену и не правя ассеты.
    /// </summary>
    public static class VisualFxCommands
    {
        /// <summary>Положить команды эффектов в набор модуля (снимаются вместе с ним).</summary>
        public static void Register(DevCommandSet set)
        {
            if (set == null) return;

            set.Add("gm_fx", "Список визуальных эффектов и их состояние", _ => List());
            set.Add("gm_fx_on", "Включить эффект по имени", a => On(a.GetString(0)),
                new DevParam("id", DevParamType.String));
            set.Add("gm_fx_off", "Выключить эффект по имени", a => Off(a.GetString(0)),
                new DevParam("id", DevParamType.String));
            set.Add("gm_fx_toggle", "Переключить эффект по имени", a => Toggle(a.GetString(0)),
                new DevParam("id", DevParamType.String));
            set.Add("gm_fx_all", "Вернуть все эффекты (как по умолчанию)", _ => All());
        }

        public static string List()
        {
            VisualToggles toggles = Toggles();
            if (toggles == null) return "Реестр эффектов недоступен (нет RootLifetimeScope?).";
            if (toggles.All.Count == 0) return "Пока ни один эффект не зарегистрирован — покажи экран, который их регистрирует.";

            var sb = new StringBuilder();
            foreach (VisualToggles.Entry e in toggles.All.OrderBy(e => e.Id))
                sb.AppendLine($"[{(e.Enabled ? "вкл" : "ВЫКЛ")}] {e.Id} — {e.Description}");
            return sb.ToString();
        }

        public static string On(string id) => Apply(id, true);

        public static string Off(string id) => Apply(id, false);

        public static string Toggle(string id)
        {
            VisualToggles toggles = Toggles();
            if (toggles == null) return "Реестр эффектов недоступен.";

            bool? now = toggles.Toggle(id);
            if (now == null) return $"Нет эффекта «{id}». Список — gm_fx.";
            return $"{id}: {(now.Value ? "включён" : "выключен")}";
        }

        public static string All()
        {
            VisualToggles toggles = Toggles();
            if (toggles == null) return "Реестр эффектов недоступен.";
            toggles.EnableAll();
            return "Все эффекты включены.";
        }

        private static string Apply(string id, bool enabled)
        {
            VisualToggles toggles = Toggles();
            if (toggles == null) return "Реестр эффектов недоступен.";
            if (!toggles.Set(id, enabled)) return $"Нет эффекта «{id}». Список — gm_fx.";
            return $"{id}: {(enabled ? "включён" : "выключен")}";
        }

        private static VisualToggles Toggles()
        {
            foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>())
            {
                if (scope.GetType().Name != "RootLifetimeScope" || scope.Container == null) continue;
                try { return scope.Container.Resolve(typeof(VisualToggles)) as VisualToggles; }
                catch { return null; }
            }
            return null;
        }
    }
}
