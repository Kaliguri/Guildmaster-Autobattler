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

            // Одна команда вместо четырёх: без аргументов — список, с id — тумблер, с явным состоянием —
            // включить/выключить. Три отдельные fx_on/fx_off/fx_toggle отличались лишь тем, что человек и
            // так держит в голове, набирая имя эффекта.
            set.Add("fx", "Эффекты: без аргументов — список, с id — переключить, с on/off — задать явно",
                a =>
                {
                    if (!a.Has(0)) return List();
                    string id = a.GetString(0);
                    return a.Has(1) ? (a.GetBool(1) ? On(id) : Off(id)) : Toggle(id);
                },
                new DevParam("id", DevParamType.String, true), new DevParam("on|off", DevParamType.Bool, true));

            set.Add("fx_reset", "Вернуть все эффекты в исходное (всё включено)", _ => All());
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
