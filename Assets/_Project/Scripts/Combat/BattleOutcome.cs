using System;

namespace Guildmaster.Combat
{
    /// <summary>Чем кончился бой: ещё идёт, победила команда, или никого не осталось.</summary>
    public enum BattleOutcomeKind
    {
        Ongoing = 0,
        TeamWin = 1,
        Draw    = 2,
    }

    /// <summary>
    /// Итог боя: «победила команда N», а не «победил игрок». Понятия «команда игрока» в бою нет —
    /// у каждого игрока своя команда (<c>ILocalPlayer.Team</c>), и победил ли ОН, решается через
    /// <see cref="IsWinFor"/>. Это шов под PvP и режимы, где игроки стоят по разные стороны.
    /// <para>Ничья (никого не осталось) победой не считается ни для кого — для игроков это поражение.</para>
    /// </summary>
    public readonly struct BattleOutcome : IEquatable<BattleOutcome>
    {
        /// <summary>Команды-победителя нет (бой идёт или ничья).</summary>
        public const int NoTeam = -1;

        public readonly BattleOutcomeKind Kind;

        /// <summary>Победившая команда или <see cref="NoTeam"/>.</summary>
        public readonly int WinningTeam;

        private BattleOutcome(BattleOutcomeKind kind, int winningTeam)
        {
            Kind        = kind;
            WinningTeam = winningTeam;
        }

        public static readonly BattleOutcome Ongoing = new BattleOutcome(BattleOutcomeKind.Ongoing, NoTeam);
        public static readonly BattleOutcome Draw    = new BattleOutcome(BattleOutcomeKind.Draw,    NoTeam);

        public static BattleOutcome Win(int team) => new BattleOutcome(BattleOutcomeKind.TeamWin, team);

        public bool IsOngoing  => Kind == BattleOutcomeKind.Ongoing;
        public bool IsFinished => Kind != BattleOutcomeKind.Ongoing;
        public bool IsDraw     => Kind == BattleOutcomeKind.Draw;

        /// <summary>Победила ли команда <paramref name="team"/>. Ничья — не победа ни для кого.</summary>
        public bool IsWinFor(int team) => Kind == BattleOutcomeKind.TeamWin && WinningTeam == team;

        public bool Equals(BattleOutcome other) => Kind == other.Kind && WinningTeam == other.WinningTeam;
        public override bool Equals(object obj) => obj is BattleOutcome other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ WinningTeam;

        public static bool operator ==(BattleOutcome a, BattleOutcome b) => a.Equals(b);
        public static bool operator !=(BattleOutcome a, BattleOutcome b) => !a.Equals(b);

        public override string ToString() => Kind switch
        {
            BattleOutcomeKind.TeamWin => $"победила команда {WinningTeam}",
            BattleOutcomeKind.Draw    => "ничья",
            _                         => "идёт",
        };
    }
}
