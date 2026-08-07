using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Где идёт игра прямо сейчас: вид мероприятия, его ограничения, открыт ли бой и в какой он фазе.
    /// Хост объявляет это состояние, гость ему следует.
    /// </summary>
    /// <remarks>
    /// <b>Почему состояние, а не события «открылось» и «закрылось».</b> Событие надо не пропустить, а
    /// гость подключается посреди игры и переживает потери — значит ему нужен ответ на вопрос «как
    /// сейчас», а не история того, как к этому пришли. Повтор того же состояния при этом ничего не
    /// стоит: применение идемпотентно.
    /// <para><b>Состав боя сюда не входит</b> — он приезжает своим каналом, по паспорту на бойца
    /// (<c>BattleRosterAnnouncer</c>). Здесь только границы: где мы и открыта ли арена.</para>
    /// </remarks>
    public readonly struct ActivityState : IEquatable<ActivityState>
    {
        public readonly ActivityKind Kind;
        public readonly bool         HideOpponent;

        /// <summary>
        /// Кому принадлежит вторая сторона арены. Едет по проводу, потому что из неё гость выводит
        /// своё право двигать бойцов — и обязан вывести ровно то же, что хозяин.
        /// </summary>
        public readonly OpposingSide Opposition;

        /// <summary>Открыта ли арена. У гостя это команда поднять свой боевой скоуп — или снести его.</summary>
        public readonly bool BattleOpen;

        /// <summary>Фаза боя у хоста. Без неё боевой UI у гостя молчал бы весь бой.</summary>
        public readonly BattlePhase Phase;

        /// <summary>
        /// Показана ли карта акта. Гость открывает и закрывает её вслед за хостом.
        /// </summary>
        /// <remarks>
        /// Карта — тоже «где мы», хотя и не мероприятие: её открывает петля акта в момент выбора узла,
        /// а петли у гостя нет. Пока этого поля не было, гость при входе в чужую кампанию оставался в
        /// пустом мире и мог найти карту только сам, табом (наход. Макса 03.08.2026).
        /// </remarks>
        public readonly bool MapOpen;

        /// <summary>
        /// Открыт ли двор гильдии. Гость открывает и закрывает его вслед за хостом.
        /// </summary>
        /// <remarks>
        /// Двор — такое же «где мы», как карта, и попал сюда по той же причине: открывает его петля
        /// игры, а петли у гостя нет. Пока поля не было, хост уходил во двор, а гость оставался в
        /// предыдущем месте — на боевой камере в пустом мире (наход. Макса 04.08.2026).
        /// </remarks>
        public readonly bool HubOpen;

        public ActivityState(ActivityKind kind, bool hideOpponent, OpposingSide opposition,
                             bool battleOpen, BattlePhase phase, bool mapOpen = false,
                             bool hubOpen = false)
        {
            Kind         = kind;
            HideOpponent = hideOpponent;
            Opposition   = opposition;
            BattleOpen   = battleOpen;
            Phase        = phase;
            MapOpen      = mapOpen;
            HubOpen      = hubOpen;
        }

        /// <summary>Как это выглядит у того, кто нигде: ни мероприятия, ни арены.</summary>
        public static ActivityState Nowhere =>
            new ActivityState(ActivityKind.None, false, OpposingSide.Encounter, false, BattlePhase.None);

        /// <summary>Настройка мероприятия без состава: состав у гостя приезжает своим каналом.</summary>
        public ActivitySetup ToSetup() => new ActivitySetup(Kind, Opposition, HideOpponent, roster: null);

        public bool Equals(ActivityState other) =>
            Kind == other.Kind && HideOpponent == other.HideOpponent &&
            Opposition == other.Opposition && BattleOpen == other.BattleOpen &&
            Phase == other.Phase && MapOpen == other.MapOpen && HubOpen == other.HubOpen;

        public override bool Equals(object obj) => obj is ActivityState other && Equals(other);

        public override int GetHashCode() =>
            ((int)Kind << 8) ^ ((int)Phase << 3) ^ ((int)Opposition << 5) ^
            (HideOpponent ? 1 : 0) ^ (BattleOpen ? 4 : 0) ^ (MapOpen ? 8 : 0) ^ (HubOpen ? 16 : 0);

        public override string ToString() =>
            $"{Kind}(бой {(BattleOpen ? "открыт" : "закрыт")}, фаза {Phase}, " +
            $"карта {(MapOpen ? "открыта" : "закрыта")}, двор {(HubOpen ? "открыт" : "закрыт")})";
    }

    /// <summary>Состояние мероприятия в байтах и обратно. Семь полей, все примитивные.</summary>
    public static class ActivityStateCodec
    {
        /// <summary>
        /// Сколько байт занимает состояние. Разбор сверяется с этим числом, а не с «хватило бы»:
        /// пакет короче — это сборка постарше, и вставать «примерно там же» нельзя.
        /// </summary>
        private const int Size = 7;

        public static ArraySegment<byte> Write(in ActivityState state, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)state.Kind);
            writer.WriteBool(state.HideOpponent);
            writer.WriteByte((byte)state.Opposition);
            writer.WriteBool(state.BattleOpen);
            writer.WriteByte((byte)state.Phase);
            writer.WriteBool(state.MapOpen);
            writer.WriteBool(state.HubOpen);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать состояние. <c>false</c> — вид мероприятия или фаза неизвестны этой сборке: это
        /// расхождение версий, и вставать «примерно там же» нельзя.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out ActivityState state)
        {
            state = ActivityState.Nowhere;
            if (payload.Count < Size) return false;

            var bytes = new NetByteReader(payload);

            byte kind  = bytes.ReadByte();
            bool hide  = bytes.ReadBool();
            byte side  = bytes.ReadByte();
            bool open  = bytes.ReadBool();
            byte phase = bytes.ReadByte();
            bool map   = bytes.ReadBool();
            bool hub   = bytes.ReadBool();

            // Приводим к ОСНОВЕ перечисления, а не к типу поля в пакете: Enum.IsDefined сверяет типы
            // строго и бросает на несовпадении. Эти два перечисления целочисленные, хотя по проводу
            // едут байтом, — и byte здесь уронил бы разбор целиком. У владельца стороны основа как раз
            // байтовая, поэтому он сверяется как есть.
            if (!Enum.IsDefined(typeof(ActivityKind), (int)kind))  return false;
            if (!Enum.IsDefined(typeof(BattlePhase), (int)phase)) return false;
            if (!Enum.IsDefined(typeof(OpposingSide), side))      return false;

            state = new ActivityState((ActivityKind)kind, hide, (OpposingSide)side, open,
                                      (BattlePhase)phase, map, hub);
            return true;
        }
    }
}
