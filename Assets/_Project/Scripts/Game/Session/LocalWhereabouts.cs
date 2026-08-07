using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

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
    /// владеет: экраны — у навигатора, карта — у шва присутствия карты, арена — у часов боя. Свой флаг
    /// «я на карте» разошёлся бы с картой ровно в тот день, когда её начнут открывать вторым способом.
    /// <para><b>Порядок проверок — это приоритет ответа,</b> и он взят из заказа: «ESC меню, Карта,
    /// Инвентарь (если над UI карты), Боевая Арена» (Макс, 07.08.2026). Инвентарь поверх карты значит
    /// «в инвентаре»: игрок смотрит туда, а не на карту.</para>
    /// <para><b>«Отошёл» сильнее всего остального</b> и ловится двумя способами: потерей фокуса и
    /// минутой без ввода. Одного фокуса мало — человек отходит от компьютера, не сворачивая игру, и
    /// ровно тогда его и ждут; окно при этом остаётся активным.</para>
    /// <para><b>Двор своего места не имеет</b> (Макс, 07.08.2026): он идёт ДО забега, и игрок там ещё
    /// не в игре — для списка это «в меню».</para>
    /// </remarks>
    public sealed class LocalWhereabouts : ILocalWhereabouts, ITickable
    {
        /// <summary>Сколько тишины считается уходом. Заказ Макса: минута.</summary>
        private const float AwayAfterSeconds = 60f;

        private readonly UI.UiNavigator      _nav;
        private readonly Flow.IActMapPresence _map;
        private readonly IActivityView       _activity;
        private readonly IBattleClock        _clock;

        private Vector2 _lastPointer;
        private float   _idleSeconds;

        public LocalWhereabouts(UI.UiNavigator nav, Flow.IActMapPresence map,
                                IActivityView activity, IBattleClock clock)
        {
            _nav      = nav;
            _map      = map;
            _activity = activity;
            _clock    = clock;
        }

        /// <summary>
        /// Копит тишину. Считается ЗДЕСЬ, а не в составе сеанса: «отошёл» — часть ответа «где я», и
        /// разводить эти два факта по разным местам значило бы дать одному вопросу двух владельцев.
        /// </summary>
        public void Tick()
        {
            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            bool touched = pointer != _lastPointer
                           || (Keyboard.current != null && Keyboard.current.anyKey.isPressed)
                           || (Mouse.current != null && Mouse.current.leftButton.isPressed);

            _lastPointer = pointer;
            _idleSeconds = touched ? 0f : _idleSeconds + Time.unscaledDeltaTime;
        }

        public PlayerWhere Current
        {
            get
            {
                if (!Application.isFocused || _idleSeconds >= AwayAfterSeconds) return PlayerWhere.Away;

                // Системное меню и настройки лежат модалью поверх всего: игрок смотрит в них.
                if (_nav != null && _nav.AnyScreen(s => s.Kind == UI.ScreenKind.Modal))
                    return PlayerWhere.Pause;

                if (HasTag(UI.UiScreen.InventoryModeTag)) return PlayerWhere.Loadout;
                if (HasTag(UI.UiScreen.MapModeTag))       return PlayerWhere.Map;
                if (_map?.IsShown == true)                return PlayerWhere.Map;

                if (_clock != null && _clock.Phase != BattlePhase.None) return PlayerWhere.Arena;

                // Мероприятия нет — игра ещё не началась или уже кончилась. Двор попадает сюда же:
                // он идёт до забега, и это тоже «ещё не в игре».
                return _activity != null && _activity.Current.IsOpen ? PlayerWhere.Arena : PlayerWhere.Menu;
            }
        }

        private bool HasTag(string tag) =>
            _nav != null && _nav.AnyScreen(s => s.ModeTag == tag);
    }
}
