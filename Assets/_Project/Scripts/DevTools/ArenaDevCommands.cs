using System.Text;
using Guildmaster.Core.Arena;
using Guildmaster.Core.DevConsole;
using Guildmaster.Presentation.Arena;
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
        /// <summary>Положить команды арены в набор модуля (снимаются вместе с ним).</summary>
        public static void Register(DevCommandSet set)
        {
            if (set == null) return;

            set.Add("arena", "Облики арены: какие есть и какой надет", _ => Skins());
            set.Add("arena_swap", "Сменить облик арены с анимацией перехода", a => Swap(a.GetString(0)),
                new DevParam("skinId", DevParamType.String));
            set.Add("arena_set", "Надеть облик мгновенно, без анимации", a => Set(a.GetString(0)),
                new DevParam("skinId", DevParamType.String));
            set.Add("arena_rush", "Доиграть идущий переход рывком", _ => Rush());
#if UNITY_EDITOR
            set.Add("arena_demo", "Собрать демо-облик «stone» из каменного тайлсета", _ => BuildDemoSkin());
#endif
        }

        public static string Skins()
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден (мир не поднят?).";

            var sb = new StringBuilder();
            sb.AppendLine($"Сейчас: {swapper.CurrentSkinId}");
            foreach (ArenaSkinSource src in Object.FindObjectsByType<ArenaSkinSource>(
                         FindObjectsInactive.Include))
                sb.AppendLine($"  {src.SkinId}{(src.IsLive ? "  (живой корень)" : "")}");
            return sb.ToString();
        }


        public static string Swap(string skinId)
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден (мир не поднят?).";
            if (swapper.Busy) return "Переход уже идёт — дождись или gm_arena_rush.";

            return swapper.Play(skinId)
                ? $"Переход на '{skinId}' пошёл ({swapper.Shape.DurationSeconds:0.0}с)."
                : $"Не выйдет: облика '{skinId}' нет либо он уже надет.";
        }

#if UNITY_EDITOR

        public static string BuildDemoSkin()
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден (мир не поднят?).";
            if (!swapper.TryGetSkin("arena", out var source)) return "Нет исходного облика 'arena'.";

            // Палитра каменного пола из пака: в рантайме её не достать, но дев-команды и живут только
            // в редакторе. Настоящая вторая арена придёт тайлмапой в сцене, это лишь стенд.
            var palette = new System.Collections.Generic.List<UnityEngine.Tilemaps.TileBase>();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:TileBase \"Stone Ground\""))
            {
                var tile = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (tile != null) palette.Add(tile);
            }
            if (palette.Count == 0) return "Каменных тайлов не нашлось — палитра пуста.";

            var variant = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>>();

            foreach (var layer in source)
            {
                var cells = new System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>();
                bool isWall = layer.Key.Contains("Wall");
                foreach (var cell in layer.Value)
                {
                    // Стены оставляем — меняется «начинка» арены, а её контур должен остаться узнаваемым.
                    cells[cell.Key] = isWall
                        ? cell.Value
                        : palette[Mathf.Abs(cell.Key.x * 73856093 ^ cell.Key.y * 19349663) % palette.Count];
                }
                variant[layer.Key] = cells;
            }

            swapper.RegisterSkin("stone", variant);
            return $"Облик 'stone' собран ({palette.Count} тайлов в палитре). Дальше: gm_arena_swap stone";
        }
#endif


        public static string Rush()
        {
            ArenaSkinSwapper swapper = Swapper();
            if (swapper == null) return "Свопер облика не найден.";
            if (!swapper.Busy) return "Переход не идёт.";

            swapper.Rush();
            return "Ускорено.";
        }


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
            foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>())
            {
                if (scope.GetType().Name != "WorldLifetimeScope" || scope.Container == null) continue;
                try { return scope.Container.Resolve(typeof(IArenaSwap)) as ArenaSkinSwapper; }
                catch { /* не зарегистрирован — упадём на поиск в сцене ниже */ }
            }
            return Object.FindAnyObjectByType<ArenaSkinSwapper>();
        }
    }
}
