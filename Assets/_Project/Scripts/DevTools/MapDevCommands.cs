using System.Text;
using Guildmaster.Game.Flow;
using Guildmaster.Presentation.Map;
using QFSW.QC;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Дев-команды карты акта: гонять фишку и смотреть переходы, не проходя забег.
    /// <para>Ключевое: переход отсюда идёт БЕЗ выбора узла (<c>PreviewTravel</c>) — иначе «просто посмотреть
    /// анимацию» уводило бы петлю забега в узел и запускало бой. Смотреть и играть — разные вещи.</para>
    /// <para>Живут отдельно от <see cref="GuildmasterCommands"/>: те инжектятся боевым скоупом и существуют
    /// только на арене, а карта — контур забега в корневом скоупе.</para>
    /// </summary>
    public static class MapDevCommands
    {
        [Command("gm_map_show", "Показать карту акта (просмотр, узлы не горят)")]
        public static string Show()
        {
            WorldMapController ctl = Controller();
            if (ctl == null) return "Нет забега: карту показывать нечего.";
            ctl.SetVisible(true);
            return "Карта показана.";
        }

        [Command("gm_map_hide", "Скрыть карту акта")]
        public static string Hide()
        {
            WorldMapController ctl = Controller();
            if (ctl == null) return "Нет забега.";
            ctl.SetVisible(false);
            return "Карта скрыта.";
        }

        [Command("gm_map_nodes", "Список узлов текущей карты (индекс + id)")]
        public static string Nodes()
        {
            IWorldMapView view = View();
            if (view == null) return "Слой карты не привязан (карта не показана?).";

            var ids = view.NodeIds;
            if (ids.Count == 0) return "Карта пуста — сначала gm_map_show.";

            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++) sb.AppendLine($"[{i}] {ids[i]}");
            return sb.ToString();
        }

        [Command("gm_map_goto", "Проехать фишкой к узлу по ИНДЕКСУ — без выбора узла (только анимация)")]
        public static string GoTo(int index)
        {
            IWorldMapView view = View();
            if (view == null) return "Слой карты не привязан (карта не показана?).";

            var ids = view.NodeIds;
            if (index < 0 || index >= ids.Count) return $"Индекс вне диапазона 0..{ids.Count - 1}. См. gm_map_nodes.";

            view.PreviewTravel(ids[index]);
            return $"Едем к [{index}] {ids[index]} (выбор не засчитывается).";
        }

        [Command("gm_map_goto_id", "Проехать фишкой к узлу по ID — без выбора узла (только анимация)")]
        public static string GoToId(string nodeId)
        {
            IWorldMapView view = View();
            if (view == null) return "Слой карты не привязан (карта не показана?).";

            view.PreviewTravel(nodeId);
            return $"Едем к {nodeId} (выбор не засчитывается).";
        }

        [Command("gm_map_reset_pawn", "Вернуть фишку на узел, где стоит отряд")]
        public static string ResetPawn()
        {
            IWorldMapView view = View();
            if (view == null) return "Слой карты не привязан (карта не показана?).";

            view.ResetPawn();
            return "Фишка возвращена на текущий узел.";
        }

        // Резолв через корневой скоуп: карта и её контроллер зарегистрированы там. Ищем по имени типа,
        // чтобы DevTools не тянул ссылку на конкретный скоуп ради одной команды.
        private static LifetimeScope Root()
        {
            foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>())
                if (scope.GetType().Name == "RootLifetimeScope") return scope;
            return null;
        }

        private static WorldMapController Controller() => Resolve<WorldMapController>();

        private static IWorldMapView View() => Resolve<IWorldMapView>();

        // Resolve, а не TryResolve: перегрузка TryResolve в этой версии VContainer требует ключ,
        // а промах регистрации для дев-команды — не ошибка, а «нечего показывать».
        private static T Resolve<T>() where T : class
        {
            LifetimeScope root = Root();
            if (root == null || root.Container == null) return null;
            try { return root.Container.Resolve(typeof(T)) as T; }
            catch { return null; }
        }
    }
}
