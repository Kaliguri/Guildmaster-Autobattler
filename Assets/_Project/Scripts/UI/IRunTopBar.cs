using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Общий контракт верхней панели забега: старая <see cref="RunTopBarView"/> и новая
    /// (app-shell) <see cref="RunModeBarView"/>. Позволяет <c>UiRootBootstrap</c> держать любую из них
    /// и кормить статами единообразно; пока новый <c>_runModeBar</c>-ассет не назначен в CoreScene,
    /// геймплей безопасно работает на старой панели (без поломки HUD на общей ветке).
    /// </summary>
    public interface IRunTopBar
    {
        VisualElement Root { get; }
        void SetGold(int gold);
        void SetAct(int actNumber);
        void SetRunTime(string timerText);
        void SetRestarts(int remaining, int max);
        void HideBattleCenter();
        void SetFighting(bool fighting, string battleTime);
    }
}
