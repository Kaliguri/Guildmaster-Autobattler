using System;
using System.Collections.Generic;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>Какой экран узла сейчас перед группой.</summary>
    /// <remarks>
    /// <b>Перечисление только ДОПИСЫВАЕТСЯ.</b> Номер едет по проводу, и переставленный номер увёл бы
    /// гостя не на тот экран — молча, потому что оба конца собираются порознь.
    /// <para><b>У каждого вида своя коробка с содержимым</b> (<see cref="RewardStage"/> и соседи), и вид
    /// решает, какую из них разбирать. Общего мешка полей здесь нет намеренно: пока содержимое ехало
    /// безымянным списком строк, «что лежит в позиции 1» знали только два места на разных концах
    /// провода — ровно тот способ разъехаться, ради запрета которого экраны и свели в один шов.</para>
    /// </remarks>
    public enum SessionStageKind : byte
    {
        /// <summary>Своего экрана у узла сейчас нет.</summary>
        None = 0,

        /// <summary>Витрина награды. Коробка — <see cref="RewardStage"/>.</summary>
        Reward = 1,

        /// <summary>Закрытый сундук: ждём, пока группа согласится его открыть. Коробки нет.</summary>
        Chest = 4,

        /// <summary>Текстовое событие. Коробка — <see cref="TextEventStage"/>.</summary>
        TextEvent = 5,

        /// <summary>Исход забега — победа или поражение. Коробка — <see cref="OutcomeStage"/>.</summary>
        Outcome = 6,

        /// <summary>
        /// Двор гильдии: дом, из которого группа уходит в забег. Коробка — <see cref="HubStage"/>.
        /// </summary>
        /// <remarks>
        /// Узлом двор не является, и в этом всё дело: шаг здесь — «что сейчас перед группой», а не
        /// «где мы на карте». Пока двор ехал своим путём (<c>ActivityState.HubOpen</c> плюс петля
        /// владельца), он был последним экраном с двумя дорогами показа — той самой формой, из-за
        /// которой разъезжались экраны узла.
        /// </remarks>
        Hub = 7,
    }

    /// <summary>
    /// Что сейчас на экране у группы — для тех, кто это ПОКАЗЫВАЕТ, независимо от роли.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 под HARD-правило «хозяин и гость — равные».</b> До него экраны узла
    /// публиковала петля акта, а она собирается только владельцу: из восьми экранов узла гость видел
    /// один. Показ переехал сюда, к общему для обеих ролей потребителю, а роли остались там, где им
    /// и место, — в том, кто ОБЪЯВЛЯЕТ шаг.
    /// <para><b>Назывался <c>NodeStage</c> и был переименован 09.08.2026:</b> имя врало. Канал живёт
    /// в сеансе, а не в узле, и несёт в том числе то, что узлом не является, — исход забега, а следом
    /// за ним и двор. Шаг здесь — это «что сейчас перед группой», без привязки к месту на карте.</para>
    /// </remarks>
    public interface ISessionStageView
    {
        /// <summary>Что объявлено сейчас.</summary>
        SessionStageState Current { get; }

        /// <summary>Шаг сменился. Повтор того же шага события не поднимает.</summary>
        event Action<SessionStageState> Changed;
    }

    /// <summary>Витрина награды: из чего выбирают и есть ли куда положить.</summary>
    public readonly struct RewardStage
    {
        /// <summary>Id выпавших реликвий, по порядку карточек.</summary>
        public readonly IReadOnlyList<string> Options;

        /// <summary>
        /// Запас реликвий полон — витрина предложит обмен вместо простого «взять».
        /// </summary>
        /// <remarks>
        /// <b>Едет по проводу, потому что от него зависит ГОЛОС.</b> При полном запасе вариант выглядит
        /// как «взять взамен того-то», при неполном — просто «взять», а согласие требует побайтово
        /// одинаковой строки у всех. Пока признак считался на каждой стороне сам, у гостя он был зашит
        /// в <c>false</c>: при полном запасе у хозяина голоса не сходились НИКОГДА, и витрина не
        /// закрывалась ни у кого — забег вставал (08.08.2026).
        /// </remarks>
        public readonly bool InventoryFull;

        public RewardStage(IReadOnlyList<string> options, bool inventoryFull)
        {
            Options       = options ?? Array.Empty<string>();
            InventoryFull = inventoryFull;
        }
    }

    /// <summary>Текстовое событие: какое показать и сколько золота у группы на руках.</summary>
    /// <remarks>
    /// <b>Золото едет вместе с событием</b>, потому что от него зависит, какие варианты ответа доступны.
    /// Считай его гость по своему снимку — он мог бы разойтись с хозяйским на кадр, и вариант «заплатить»
    /// оказался бы у одного живым, у другого серым.
    /// </remarks>
    public readonly struct TextEventStage
    {
        public readonly string EventId;
        public readonly int    Gold;

        public TextEventStage(string eventId, int gold)
        {
            EventId = eventId ?? string.Empty;
            Gold    = gold;
        }
    }

    /// <summary>Исход забега: дошли или нет.</summary>
    public readonly struct OutcomeStage
    {
        public readonly bool Victory;

        public OutcomeStage(bool victory) => Victory = victory;
    }

    /// <summary>Двор гильдии: чей дом перед группой.</summary>
    /// <remarks>
    /// <b>Имя дома едет по проводу</b>, и это не украшение: пока двор показывался гостю отдельным
    /// путём, имени у него не было вовсе — <c>OpenHubRequest(null, ...)</c>. Хозяин видел «Дом
    /// Алебардиум», гость — пустое место на том же экране, и разницу эту никто не заказывал.
    /// </remarks>
    public readonly struct HubStage
    {
        /// <summary>Имя дома, из которого уходят в забег.</summary>
        public readonly string GuildName;

        public HubStage(string guildName) => GuildName = guildName;
    }

    /// <summary>
    /// Хвост узла: узел пройден, дальше — кнопки «Продолжить» и «К построению», а под ними, если узел
    /// оставил, кадр-прощание.
    /// </summary>
    /// <remarks>
    /// <b>Это ХВОСТ, а не отдельный экран</b>, и в этом всё дело. Кнопки ложатся ПОВЕРХ того, что уже
    /// на экране: у сундука под ними кадр-прощание, у текстового события — само событие с текстом
    /// результата, у боя — арена. Сделай мы конец узла ещё одним видом экрана, он молча стирал бы то,
    /// поверх чего должен лечь.
    /// <para><b>Решения тут нет — это навигация</b> (вердикт Макса 08.08.2026: «Каждый нажимает отдельно
    /// чисто для себя»). Кнопки ведут туда же, куда табы, и жмут их порознь: ждать напарника, чтобы
    /// посмотреть свой строй, незачем.</para>
    /// </remarks>
    public readonly struct NodeRest
    {
        /// <summary>Узел пройден: показываем кнопки «дальше».</summary>
        public readonly bool Ended;

        /// <summary>Заголовок кадра-прощания; пусто — кадра нет.</summary>
        public readonly string TitleKey;

        /// <summary>Тело кадра-прощания; пусто — кадра нет.</summary>
        public readonly string BodyKey;

        public NodeRest(bool ended, string titleKey, string bodyKey)
        {
            Ended    = ended;
            TitleKey = titleKey ?? string.Empty;
            BodyKey  = bodyKey  ?? string.Empty;
        }

        /// <summary>Есть ли чем проводить узел. Бой не оставляет ничего — исход показан своим экраном.</summary>
        public bool HasFarewell => Ended && (TitleKey.Length > 0 || BodyKey.Length > 0);
    }

    /// <summary>
    /// Шаг узла: какой экран перед группой, что на нём и пройден ли узел.
    /// </summary>
    /// <remarks>
    /// <b>Состояние, а не приказ «покажи».</b> Гость подключается в любой момент и переживает потери,
    /// поэтому ему нужен ответ на «что сейчас», а не история того, как к этому пришли. Применение
    /// идемпотентно: повтор того же шага ничего не открывает заново.
    /// <para><b>Коробка хранится упакованной</b>, а распаковывается по виду (<c>TryOpenReward</c> и
    /// соседи). Так у владельца и у гостя один и тот же путь к экрану целиком, вместе с разбором:
    /// собери владелец свою коробку в обход провода — и разошлись бы они ровно там, где никто не
    /// смотрит.</para>
    /// <para><b>Экран и хвост — два разных факта</b>, потому что на экране они и живут вместе. Поэтому
    /// хвост навешивается на текущий шаг (<see cref="EndingNode"/>), а не подменяет его.</para>
    /// </remarks>
    public readonly struct SessionStageState : IEquatable<SessionStageState>
    {
        public readonly SessionStageKind Kind;

        /// <summary>Пройден ли узел и чем его проводить.</summary>
        public readonly NodeRest Rest;

        private readonly byte[] _payload;

        private static readonly byte[] Empty = Array.Empty<byte>();

        internal SessionStageState(SessionStageKind kind, byte[] payload, NodeRest rest = default)
        {
            Kind     = kind;
            _payload = payload ?? Empty;
            Rest     = rest;
        }

        /// <summary>Упакованная коробка вида. Пусто — у видов, которым нечего нести.</summary>
        internal ArraySegment<byte> Payload => new ArraySegment<byte>(_payload);

        /// <summary>Экрана нет и узел идёт своим ходом.</summary>
        public static SessionStageState Idle => new SessionStageState(SessionStageKind.None, Empty);

        /// <summary>Закрытый сундук.</summary>
        public static SessionStageState Chest => new SessionStageState(SessionStageKind.Chest, Empty);

        public static SessionStageState Reward(IReadOnlyList<string> options, bool inventoryFull)
        {
            var writer = new NetByteWriter(64);
            writer.WriteBool(inventoryFull);
            writer.WriteByte((byte)(options.Count > 255 ? 255 : options.Count));
            for (int i = 0; i < options.Count && i < 255; i++) writer.WriteString(options[i]);

            return new SessionStageState(SessionStageKind.Reward, Pack(writer));
        }

        public static SessionStageState TextEvent(string eventId, int gold)
        {
            var writer = new NetByteWriter(64);
            writer.WriteString(eventId ?? string.Empty);
            writer.WriteInt(gold);

            return new SessionStageState(SessionStageKind.TextEvent, Pack(writer));
        }

        public static SessionStageState Outcome(bool victory)
        {
            var writer = new NetByteWriter(16);
            writer.WriteBool(victory);

            return new SessionStageState(SessionStageKind.Outcome, Pack(writer));
        }

        /// <summary>Двор гильдии перед группой: дом, из которого уходят вместе.</summary>
        public static SessionStageState Hub(string guildName)
        {
            var writer = new NetByteWriter(64);
            writer.WriteString(guildName ?? string.Empty);

            return new SessionStageState(SessionStageKind.Hub, Pack(writer));
        }

        /// <summary>
        /// Тот же экран, но узел пройден: сверху кнопки «дальше», снизу — кадр-прощание, если узел его
        /// оставил.
        /// </summary>
        /// <remarks>
        /// Петля зовёт это на ТЕКУЩЕМ шаге, а не объявляет свой: экран под кнопками принадлежит узлу, и
        /// петля про него ничего не знает. Ключи задаёт тот, кто узел вёл.
        /// <para><b><c>null</c> — «оставить как есть», а не «стереть».</b> Петля вешает кнопки без
        /// аргументов, и узел к этому моменту мог уже положить свой кадр-прощание. Затирай мы его
        /// пустыми строками — кадр сундука исчезал бы ровно в тот момент, когда петля добавляет
        /// кнопки.</para>
        /// </remarks>
        public SessionStageState EndingNode(string titleKey = null, string bodyKey = null) =>
            new SessionStageState(Kind, _payload,
                new NodeRest(true, titleKey ?? Rest.TitleKey, bodyKey ?? Rest.BodyKey));

        /// <summary>Разобрать витрину. <c>false</c> — сейчас на экране не она.</summary>
        public bool TryOpenReward(out RewardStage box)
        {
            box = default;
            if (Kind != SessionStageKind.Reward) return false;

            var bytes = new NetByteReader(Payload);
            try
            {
                bool full  = bytes.ReadBool();
                int  count = bytes.ReadByte();
                var options = new List<string>(count);
                for (int i = 0; i < count; i++) options.Add(bytes.ReadString());

                box = new RewardStage(options, full);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Разобрать текстовое событие. <c>false</c> — сейчас на экране не оно.</summary>
        public bool TryOpenTextEvent(out TextEventStage box)
        {
            box = default;
            if (Kind != SessionStageKind.TextEvent) return false;

            var bytes = new NetByteReader(Payload);
            try
            {
                box = new TextEventStage(bytes.ReadString(), bytes.ReadInt());
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Разобрать исход забега. <c>false</c> — сейчас на экране не он.</summary>
        public bool TryOpenOutcome(out OutcomeStage box)
        {
            box = default;
            if (Kind != SessionStageKind.Outcome) return false;

            var bytes = new NetByteReader(Payload);
            try
            {
                box = new OutcomeStage(bytes.ReadBool());
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Разобрать двор. <c>false</c> — сейчас на экране не он.</summary>
        public bool TryOpenHub(out HubStage box)
        {
            box = default;
            if (Kind != SessionStageKind.Hub) return false;

            var bytes = new NetByteReader(Payload);
            try
            {
                box = new HubStage(bytes.ReadString());
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>
        /// Разбирается ли коробка этого вида. Спрашивает приёмник, а не показ: расхождение версий надо
        /// поймать на входе, пока ещё можно оставить экран прежним.
        /// </summary>
        internal bool IsWellFormed() => Kind switch
        {
            SessionStageKind.Reward    => TryOpenReward(out _),
            SessionStageKind.TextEvent => TryOpenTextEvent(out _),
            SessionStageKind.Outcome   => TryOpenOutcome(out _),
            SessionStageKind.Hub       => TryOpenHub(out _),
            _                       => _payload.Length == 0, // видам без коробки нести нечего
        };

        private static byte[] Pack(NetByteWriter writer)
        {
            ArraySegment<byte> written = writer.WrittenSegment;
            var packed = new byte[written.Count];
            Array.Copy(written.Array, written.Offset, packed, 0, written.Count);
            return packed;
        }

        /// <summary>
        /// Тот же шаг — это тот же экран с той же коробкой, байт в байт, и тот же хвост.
        /// </summary>
        /// <remarks>
        /// Сравнение упакованного, а не полей: смысл равенства здесь — «гостю нечего перерисовывать»,
        /// а гость видит ровно эти байты. Заведи мы сравнение по полям — оно разошлось бы с тем, что
        /// реально едет, на первом же поле, которое забыли учесть.
        /// </remarks>
        public bool Equals(SessionStageState other)
        {
            if (Kind != other.Kind) return false;
            if (Rest.Ended != other.Rest.Ended || Rest.TitleKey != other.Rest.TitleKey ||
                Rest.BodyKey != other.Rest.BodyKey) return false;

            byte[] mine = _payload ?? Empty, theirs = other._payload ?? Empty;
            if (mine.Length != theirs.Length) return false;

            for (int i = 0; i < mine.Length; i++)
                if (mine[i] != theirs[i]) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is SessionStageState other && Equals(other);

        public override int GetHashCode()
        {
            int hash = (int)Kind * 397 ^ (Rest.Ended ? 8191 : 0);
            byte[] mine = _payload ?? Empty;
            for (int i = 0; i < mine.Length; i++) hash = (hash * 31) ^ mine[i];
            if (Rest.TitleKey.Length > 0) hash = (hash * 31) ^ Rest.TitleKey.GetHashCode();
            return hash;
        }

        public override string ToString()
        {
            string screen;
            switch (Kind)
            {
                case SessionStageKind.Reward when TryOpenReward(out RewardStage shelf):
                    screen = $"Reward({string.Join(", ", shelf.Options)})" +
                             (shelf.InventoryFull ? " [запас полон]" : string.Empty);
                    break;
                case SessionStageKind.TextEvent when TryOpenTextEvent(out TextEventStage ev):
                    screen = $"TextEvent({ev.EventId}, золота {ev.Gold})";
                    break;
                case SessionStageKind.Outcome when TryOpenOutcome(out OutcomeStage outcome):
                    screen = outcome.Victory ? "Outcome(победа)" : "Outcome(поражение)";
                    break;
                case SessionStageKind.Hub when TryOpenHub(out HubStage hub):
                    screen = $"Hub({hub.GuildName})";
                    break;
                default:
                    screen = Kind.ToString();
                    break;
            }

            if (!Rest.Ended) return screen;

            return Rest.HasFarewell ? $"{screen} + конец узла ({Rest.TitleKey})" : $"{screen} + конец узла";
        }
    }

    /// <summary>Шаг узла в байтах и обратно.</summary>
    public static class SessionStageCodec
    {
        public static ArraySegment<byte> Write(in SessionStageState state, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)state.Kind);

            // Длина коробки нужна потому, что за ней едет хвост узла: без неё разбор не знал бы, где
            // кончается экран и начинается «узел пройден».
            ArraySegment<byte> box = state.Payload;
            writer.WriteUShort((ushort)box.Count);
            for (int i = 0; i < box.Count; i++) writer.WriteByte(box.Array[box.Offset + i]);

            writer.WriteBool(state.Rest.Ended);
            if (state.Rest.Ended)
            {
                writer.WriteString(state.Rest.TitleKey);
                writer.WriteString(state.Rest.BodyKey);
            }

            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать шаг. <c>false</c> — вид экрана неизвестен этой сборке или коробка не разбирается:
        /// это расхождение версий, и показывать «примерно то же» нельзя.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out SessionStageState state)
        {
            state = SessionStageState.Idle;

            var bytes = new NetByteReader(payload);
            byte kind;
            byte[] box;
            var rest = default(NodeRest);
            try
            {
                kind = bytes.ReadByte();
                int size = bytes.ReadUShort();

                ArraySegment<byte> raw = bytes.ReadBytes(size);
                box = new byte[size];
                if (size > 0) Array.Copy(raw.Array, raw.Offset, box, 0, size);

                if (bytes.ReadBool()) rest = new NodeRest(true, bytes.ReadString(), bytes.ReadString());
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(SessionStageKind), kind)) return false;

            var read = new SessionStageState((SessionStageKind)kind, box, rest);
            if (!read.IsWellFormed()) return false;

            state = read;
            return true;
        }
    }
}
