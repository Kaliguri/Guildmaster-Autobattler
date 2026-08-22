using System.Collections.Generic;
using Guildmaster.Core.Players;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Guildmaster.UI.Presence
{
    /// <summary>
    /// Список участников сеанса слева под верхней панелью: мейн-цвет, ник, своя строка выделена.
    /// </summary>
    /// <remarks>
    /// <b>Зачем он, если курсоры и так видно.</b> Курсор отвечает «где человек прямо сейчас» и только
    /// пока тот на твоём экране; ушедший в лавку или на карту исчезает, и это читается как «он вышел».
    /// Список отвечает на вопрос «кто с нами» в любую секунду — и он же будущий дом для признаков
    /// «выбирает», «готов», «отошёл» и значка «где он» (кооп-кластер ГДД, presence §Куда ушёл игрок).
    /// <para><b>В одиночку панель скрыта целиком.</b> Список из одного себя не сообщает ничего и просто
    /// занимает угол экрана — тот самый, который до 03.08.2026 занимала dev-подпись оверлеев.</para>
    /// <para><b>Перестраивается по отпечатку, а не каждый кадр.</b> Состав меняется редко (вход, выход,
    /// смена цвета), а пересборка элементов на каждом кадре сбрасывала бы hover и стоила бы мусора на
    /// ровном месте.</para>
    /// <para><b>Две группы в одной панели слева</b> (решение Макса 03.08.2026): сверху своя сторона,
    /// под разделителем — противники. Панель у правого края читалась бы как сторона арены, но стоила бы
    /// целого угла экрана; в кампании второй группы нет вовсе, и слово «Против» там было бы неправдой.</para>
    /// <para><b>Себя подписываем словом, а не только начертанием:</b> цвет игрок выбирал в профиле и
    /// мог его забыть — тем более что хост вправе подвинуть выбор из-за конфликта с чужим.</para>
    /// </remarks>
    public sealed class ParticipantsPanelView : ITickable
    {
        private readonly ISessionRoster _roster;

        private VisualElement _root;
        private int           _fingerprint = -1;

        public ParticipantsPanelView(ISessionRoster roster) => _roster = roster;

        /// <summary>Взять место у корня UI. До этого рисовать некуда.</summary>
        public void Attach(VisualElement layer)
        {
            if (layer == null) return;

            _root = new VisualElement { name = "participants", pickingMode = PickingMode.Ignore };
            _root.AddToClassList("gm-participants");
            _root.style.display = DisplayStyle.None;

            layer.Add(_root);
        }

        public void Tick()
        {
            if (_root == null) return;

            IReadOnlyList<SessionPlayer> players = _roster?.Players;
            int count = players?.Count ?? 0;

            // Одному список не нужен: он не отвечает ни на один вопрос, ради которых заведён.
            if (count < 2)
            {
                if (_root.style.display != DisplayStyle.None)
                {
                    _root.style.display = DisplayStyle.None;
                    _root.Clear();
                    _fingerprint = -1;
                }
                return;
            }

            int print = Fingerprint(players, _roster.LocalId);
            if (print == _fingerprint) return;

            _fingerprint = print;
            Rebuild(players);
            _root.style.display = DisplayStyle.Flex;
        }

        private void Rebuild(IReadOnlyList<SessionPlayer> players)
        {
            _root.Clear();

            int localId   = _roster.LocalId;
            int localTeam = _roster.TryGet(localId, out SessionPlayer me) ? me.Team : 0;

            var allies = new VisualElement { name = "participants-allies", pickingMode = PickingMode.Ignore };
            allies.AddToClassList("gm-participants__group");

            var foes = new VisualElement { name = "participants-foes", pickingMode = PickingMode.Ignore };
            foes.AddToClassList("gm-participants__group");

            for (int i = 0; i < players.Count; i++)
            {
                SessionPlayer player = players[i];
                bool ally = player.Team == localTeam;

                (ally ? allies : foes).Add(Row(player, isLocal: player.Id == localId));
            }

            _root.Add(allies);

            // Заголовок и вторая группа появляются только когда противники есть: в кампании стороны
            // одна на всех, и разделитель там разделял бы пустоту.
            if (foes.childCount > 0)
            {
                var caption = new Label("Против") { pickingMode = PickingMode.Ignore };
                caption.AddToClassList("gm-text-label");
                caption.AddToClassList("gm-text--muted");
                caption.AddToClassList("gm-participants__caption");
                _root.Add(caption);
                _root.Add(foes);
            }
        }

        private static VisualElement Row(in SessionPlayer player, bool isLocal)
        {
            var row = new VisualElement { name = $"participant-{player.Id}", pickingMode = PickingMode.Ignore };
            row.AddToClassList("gm-participants__row");
            if (isLocal) row.AddToClassList("gm-participants__row--self");

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("gm-participants__dot");
            dot.AddToClassList("gm-cursor--" + Core.Players.PlayerColors.SuffixOf(player.ColorIndex));

            var name = new Label(player.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("gm-text-label");
            name.AddToClassList("gm-participants__name");

            row.Add(dot);
            row.Add(name);

            // Где он сейчас — ответ на самый частый вопрос кооп-вечера, и потому он в строке, а не
            // всплывает моментом. Своё место не подписываем: игрок и так знает, где он.
            if (!isLocal)
            {
                var where = new Label(WhereLabel(player.Where)) { pickingMode = PickingMode.Ignore };
                where.AddToClassList("gm-text-label");
                where.AddToClassList("gm-text--muted");
                where.AddToClassList("gm-participants__where");
                row.Add(where);
            }

            if (isLocal)
            {
                var you = new Label("вы") { pickingMode = PickingMode.Ignore };
                you.AddToClassList("gm-text-label");
                you.AddToClassList("gm-text--muted");
                you.AddToClassList("gm-participants__you");
                row.Add(you);
            }

            return row;
        }

        /// <summary>
        /// Подпись места. Словом, а не иконкой: значков на восемь состояний пришлось бы рисовать восемь,
        /// а читаются они хуже короткого слова — и это ещё до того, как их начнут путать между собой.
        /// </summary>
        private static string WhereLabel(PlayerWhere where)
        {
            switch (where)
            {
                case PlayerWhere.Away:    return "отошёл";
                case PlayerWhere.Menu:    return "в меню";
                case PlayerWhere.Map:     return "на карте";
                case PlayerWhere.Arena:   return "на арене";
                case PlayerWhere.Loadout: return "в инвентаре";
                case PlayerWhere.Pause:   return "в ESC меню";
                default:                  return string.Empty; // ещё не сказал о себе — молчим
            }
        }

        /// <summary>
        /// Дешёвая свёртка состава: кто, каким цветом и под каким именем. Имя входит хешем — смена ника
        /// доезжает отдельным сообщением уже ПОСЛЕ входа, и без него строка осталась бы «Игрок 2».
        /// </summary>
        private static int Fingerprint(IReadOnlyList<SessionPlayer> players, int localId)
        {
            // Свой номер входит в свёртку, потому что от него зависит РАЗБИВКА на группы: у гостя он
            // приезжает рукопожатием уже после первых пакетов состава.
            int hash = 17 * 31 + localId;
            for (int i = 0; i < players.Count; i++)
            {
                SessionPlayer player = players[i];
                hash = hash * 31 + player.Id;
                hash = hash * 31 + player.ColorIndex;
                hash = hash * 31 + player.Team;
                hash = hash * 31 + (int)player.Where; // без этого смена места не перерисовала бы строку
                hash = hash * 31 + (player.Name?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}
