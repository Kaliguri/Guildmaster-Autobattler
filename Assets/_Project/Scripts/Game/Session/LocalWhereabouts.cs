using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Game.Session
{
    /// <summary>Где я сейчас — для строки списка участников у остальных.</summary>
    public interface ILocalWhereabouts
    {
        /// <summary>Место на этот кадр.</summary>
        PlayerWhere Current { get; }
    }

    /// <summary>
    /// Считает своё место по тому, что УЖЕ знают показ и навигатор, а не по собственному флагу.
    /// </summary>
    /// <remarks>
    /// <b>Второго владельца «где я» быть не должно.</b> Каждый признак здесь спрашивается у того, кто им
    /// владеет: экраны — у навигатора, карта и двор — у своих швов присутствия, арена — у часов боя.
    /// Свой флаг «я на карте» разошёлся бы с картой ровно в тот день, когда её начнут открывать вторым
    /// способом.
    /// <para><b>Порядок проверок — это приоритет ответа,</b> и он взят из заказа: «ESC меню, Карта,
    /// Инвентарь (если над UI карты), Боевая Арена» (Макс, 07.08.2026). Инвентарь поверх карты значит
    /// «в инвентаре»: игрок смотрит туда, а не на карту.</para>
    /// <para><b>«Отошёл» сильнее всего остального:</b> свёрнутое окно — это не место, и показывать
    /// такого игрока «на карте» значит ждать от него ответа, которого не будет.</para>
    /// </remarks>
    public sealed class LocalWhereabouts : ILocalWhereabouts
    {
        private readonly UI.UiNavigator _nav;
        private readonly Flow.IActMapPresence      _map;
        private readonly Core.Flow.IHubPresence    _hub;
        private readonly IActivityView             _activity;
        private readonly IBattleClock              _clock;

        public LocalWhereabouts(UI.UiNavigator nav, Flow.IActMapPresence map,
                                Core.Flow.IHubPresence hub, IActivityView activity, IBattleClock clock)
        {
            _nav      = nav;
            _map      = map;
            _hub      = hub;
            _activity = activity;
            _clock    = clock;
        }

        public PlayerWhere Current
        {
            get
            {
                if (!Application.isFocused) return PlayerWhere.Away;

                // Системное меню и настройки лежат модалью поверх всего: игрок смотрит в них.
                if (_nav != null && _nav.AnyScreen(s => s.Kind == UI.ScreenKind.Modal))
                    return PlayerWhere.Pause;

                if (HasTag(UI.UiScreen.InventoryModeTag)) return PlayerWhere.Loadout;
                if (HasTag(UI.UiScreen.MapModeTag))       return PlayerWhere.Map;

                if (_map?.IsShown == true) return PlayerWhere.Map;
                if (_hub?.IsShown == true) return PlayerWhere.Courtyard;

                if (_clock != null && _clock.Phase != BattlePhase.None) return PlayerWhere.Arena;

                // Мероприятия нет — значит игра ещё не началась или уже кончилась: игрок в меню.
                return _activity != null && _activity.Current.IsOpen ? PlayerWhere.Arena : PlayerWhere.Menu;
            }
        }

        private bool HasTag(string tag) =>
            _nav != null && _nav.AnyScreen(s => s.ModeTag == tag);
    }
}
