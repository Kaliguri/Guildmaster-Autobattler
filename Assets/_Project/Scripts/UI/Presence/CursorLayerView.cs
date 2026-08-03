using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Guildmaster.UI.Presence
{
    /// <summary>
    /// Курсоры остальных игроков поверх мира: мейн-цвет, подпись именем, приглушение в бою.
    /// </summary>
    /// <remarks>
    /// <b>Рисуется в UI, хотя живёт в мировых координатах.</b> Курсор обязан читаться одинаково при любом
    /// зуме, нести подпись и не участвовать в сортировке спрайтов — это интерфейс, а не объект арены.
    /// Перевод «мир → панель» делает <see cref="RuntimePanelUtils"/>, поэтому указатель стоит на том же
    /// бойце, что и у хозяина, при разном разрешении и разном положении камеры.
    /// <para><b>В бою курсор приглушается, а не гаснет</b> (решение Макса 03.08.2026, отменяет прежнее
    /// правило кооп-кластера): исчезнувшие курсоры читаются как «все разошлись», а яркие мешают смотреть
    /// драку. Подпись при этом убирается — имя в бою уже не нужно, оно есть в списке участников.</para>
    /// <para><b>Кого показывать, решено ДО нас.</b> Пакет присутствия режется по сторонам у хоста; здесь
    /// нет и не должно быть проверки «а не противник ли он» — вторая такая проверка стала бы вторым
    /// владельцем правила, и разошлись бы они молча.</para>
    /// </remarks>
    public sealed class CursorLayerView : ITickable
    {
        /// <summary>Сколько курсор держится прозрачным в бою. Не тумблер: значение подобрано глазом.</summary>
        private const float BattleOpacity = 0.45f;

        private readonly IPresenceView    _presence;
        private readonly ISessionRoster   _roster;
        private readonly ISettingsService _settings;
        private readonly IBattleClock     _clock;
        private readonly CursorSkinCatalog _skins;

        private readonly Dictionary<int, CursorVisual> _cursors = new Dictionary<int, CursorVisual>(4);
        private readonly List<int>                     _stale   = new List<int>(4);

        private VisualElement _layer;

        public CursorLayerView(IPresenceView presence, ISessionRoster roster,
                               ISettingsService settings, IBattleClock clock,
                               CursorSkinCatalog skins)
        {
            _presence = presence;
            _roster   = roster;
            _settings = settings;
            _clock    = clock;
            _skins    = skins;
        }

        /// <summary>Каким скином играет этот участник. Пусто — умолчание набора решит каталог.</summary>
        private string SkinOf(int playerId) =>
            _roster != null && _roster.TryGet(playerId, out SessionPlayer player) ? player.CursorSkinId : null;

        /// <summary>Взять слой курсоров у корня UI. До этого рисовать некуда — и незачем.</summary>
        public void Attach(VisualElement layer) => _layer = layer;

        public void Tick()
        {
            if (_layer == null) return;

            bool hidden = _settings != null && _settings.Gameplay.HideOtherCursors;
            int  count  = hidden ? 0 : (_presence?.Count ?? 0);

            UnityEngine.Camera camera = UnityEngine.Camera.main;
            bool inBattle = _clock != null && _clock.Phase == BattlePhase.Fighting;

            for (int i = 0; i < count; i++)
            {
                RemoteCursor remote = _presence[i];
                CursorVisual cursor = Ensure(remote.PlayerId);

                cursor.Seen = true;
                Place(cursor, remote, camera);
                Dress(cursor, inBattle);
            }

            Sweep();
        }

        private CursorVisual Ensure(int playerId)
        {
            if (_cursors.TryGetValue(playerId, out CursorVisual known)) return known;

            var root = new VisualElement { name = $"cursor-p{playerId}", pickingMode = PickingMode.Ignore };
            root.AddToClassList("gm-cursor");

            var point = new VisualElement { name = "point", pickingMode = PickingMode.Ignore };
            point.AddToClassList("gm-cursor__point");

            // Картинка приходит из данных скина, а цвет остаётся в USS: так палитра не теряет владение
            // цветом, а набор скинов пополняется ассетом без правки стилей.
            Texture2D texture = _skins != null ? _skins.Resolve(SkinOf(playerId))?.Texture : null;
            if (texture != null) point.style.backgroundImage = new StyleBackground(texture);

            var label = new Label { name = "name", pickingMode = PickingMode.Ignore };
            label.AddToClassList("gm-cursor__name");

            root.Add(point);
            root.Add(label);

            // Мейн-цвет приезжает местом в наборе, а сам оттенок живёт в USS — палитра остаётся
            // единственным владельцем цвета даже здесь, где значение пришло по сети.
            int slot = _roster != null && _roster.TryGet(playerId, out SessionPlayer player)
                ? player.ColorIndex
                : playerId;
            root.AddToClassList($"gm-cursor--p{(slot % 4) + 1}");

            label.text = _roster != null && _roster.TryGet(playerId, out SessionPlayer named)
                ? named.Name
                : $"Игрок {playerId + 1}";

            _layer.Add(root);

            var cursor = new CursorVisual { Root = root, Name = label };
            _cursors[playerId] = cursor;
            return cursor;
        }

        private static void Place(CursorVisual cursor, in RemoteCursor remote, UnityEngine.Camera camera)
        {
            if (camera == null || cursor.Root.panel == null) return;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(
                cursor.Root.panel, remote.Position, camera);

            cursor.Root.style.left = panelPoint.x;
            cursor.Root.style.top  = panelPoint.y;

            cursor.Root.EnableInClassList("gm-cursor--holding", remote.IsHolding);
        }

        private static void Dress(CursorVisual cursor, bool inBattle)
        {
            cursor.Root.style.opacity   = inBattle ? BattleOpacity : 1f;
            cursor.Name.style.display   = inBattle ? DisplayStyle.None : DisplayStyle.Flex;
            cursor.Root.style.display   = DisplayStyle.Flex;
        }

        /// <summary>Убрать курсоры тех, кого в этом кадре не было: игрок вышел, сменил сторону или ушёл в меню.</summary>
        private void Sweep()
        {
            _stale.Clear();

            foreach (KeyValuePair<int, CursorVisual> pair in _cursors)
            {
                if (pair.Value.Seen)
                {
                    pair.Value.Seen = false;
                    continue;
                }

                pair.Value.Root.RemoveFromHierarchy();
                _stale.Add(pair.Key);
            }

            for (int i = 0; i < _stale.Count; i++) _cursors.Remove(_stale[i]);
        }

        /// <summary>Элементы одного курсора. Класс, а не структура: <c>Seen</c> правится на месте.</summary>
        private sealed class CursorVisual
        {
            public VisualElement Root;
            public Label         Name;
            public bool          Seen;
        }
    }
}
