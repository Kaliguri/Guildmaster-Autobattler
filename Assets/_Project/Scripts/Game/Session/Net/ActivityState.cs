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
        public readonly bool         OwnUnitsOnly;

        /// <summary>Открыта ли арена. У гостя это команда поднять свой боевой скоуп — или снести его.</summary>
        public readonly bool BattleOpen;

        /// <summary>Фаза боя у хоста. Без неё боевой UI у гостя молчал бы весь бой.</summary>
        public readonly BattlePhase Phase;

        public ActivityState(ActivityKind kind, bool hideOpponent, bool ownUnitsOnly,
                             bool battleOpen, BattlePhase phase)
        {
            Kind         = kind;
            HideOpponent = hideOpponent;
            OwnUnitsOnly = ownUnitsOnly;
            BattleOpen   = battleOpen;
            Phase        = phase;
        }

        /// <summary>Как это выглядит у того, кто нигде: ни мероприятия, ни арены.</summary>
        public static ActivityState Nowhere =>
            new ActivityState(ActivityKind.None, false, false, false, BattlePhase.None);

        /// <summary>Настройка мероприятия без состава: состав у гостя приезжает своим каналом.</summary>
        public ActivitySetup ToSetup() => new ActivitySetup(Kind, HideOpponent, OwnUnitsOnly, roster: null);

        public bool Equals(ActivityState other) =>
            Kind == other.Kind && HideOpponent == other.HideOpponent &&
            OwnUnitsOnly == other.OwnUnitsOnly && BattleOpen == other.BattleOpen && Phase == other.Phase;

        public override bool Equals(object obj) => obj is ActivityState other && Equals(other);

        public override int GetHashCode() =>
            ((int)Kind << 8) ^ ((int)Phase << 3) ^ (HideOpponent ? 1 : 0) ^
            (OwnUnitsOnly ? 2 : 0) ^ (BattleOpen ? 4 : 0);

        public override string ToString() =>
            $"{Kind}(бой {(BattleOpen ? "открыт" : "закрыт")}, фаза {Phase})";
    }

    /// <summary>Состояние мероприятия в байтах и обратно. Пять полей, все примитивные.</summary>
    public static class ActivityStateCodec
    {
        public static ArraySegment<byte> Write(in ActivityState state, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)state.Kind);
            writer.WriteBool(state.HideOpponent);
            writer.WriteBool(state.OwnUnitsOnly);
            writer.WriteBool(state.BattleOpen);
            writer.WriteByte((byte)state.Phase);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать состояние. <c>false</c> — вид мероприятия или фаза неизвестны этой сборке: это
        /// расхождение версий, и вставать «примерно там же» нельзя.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out ActivityState state)
        {
            state = ActivityState.Nowhere;
            if (payload.Count < 5) return false;

            var bytes = new NetByteReader(payload);

            byte kind = bytes.ReadByte();
            bool hide  = bytes.ReadBool();
            bool own   = bytes.ReadBool();
            bool open  = bytes.ReadBool();
            byte phase = bytes.ReadByte();

            // Приводим к ОСНОВЕ перечисления, а не к типу поля в пакете: Enum.IsDefined сверяет типы
            // строго и бросает на несовпадении. Оба этих перечисления целочисленные, хотя по проводу
            // едут байтом, — и byte здесь уронил бы разбор целиком.
            if (!Enum.IsDefined(typeof(ActivityKind), (int)kind))  return false;
            if (!Enum.IsDefined(typeof(BattlePhase), (int)phase)) return false;

            state = new ActivityState((ActivityKind)kind, hide, own, open, (BattlePhase)phase);
            return true;
        }
    }
}
