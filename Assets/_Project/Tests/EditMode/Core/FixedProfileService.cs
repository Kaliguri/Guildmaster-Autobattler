using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Тестовый <see cref="IProfileService"/> с ОДНИМ фиксированным профилем и гильдией. Тестам забега
    /// иерархия не интересна — им нужен только валидный ключ сейва; поведение самих профилей проверяет
    /// <c>ProfileServiceTests</c> на настоящей реализации.
    /// <para><see cref="RunKey"/> по умолчанию указывает в тестовую гильдию. Пустой ключ («активной
    /// гильдии нет») задаётся через <see cref="WithoutGuild"/> — это отдельная ветка поведения забега.</para>
    /// </summary>
    public sealed class FixedProfileService : IProfileService
    {
        private readonly ProfileSummary _profile = new("test-profile", "Тестовый профиль");
        private readonly ProfileSummary _guild   = new("test-guild", "Тестовая гильдия");
        private readonly bool _hasGuild;

        public FixedProfileService(bool hasGuild = true) => _hasGuild = hasGuild;

        /// <summary>Профиль без активной гильдии: забегу писать некуда.</summary>
        public static FixedProfileService WithoutGuild() => new(hasGuild: false);

        public IReadOnlyList<ProfileSummary> Profiles => new[] { _profile };
        public ProfileSummary ActiveProfile => _profile;

        public IReadOnlyList<ProfileSummary> Guilds =>
            _hasGuild ? new[] { _guild } : Array.Empty<ProfileSummary>();

        public ProfileSummary ActiveGuild => _hasGuild ? _guild : default;

        public bool ProfilesFull => false;
        public bool GuildsFull   => false;

        public string RunKey => _hasGuild
            ? $"profiles/{_profile.Id}/guilds/{_guild.Id}/run"
            : string.Empty;

        public bool HasActiveProfile => true;

        public ProfileIdentity Identity { get; private set; } =
            new ProfileIdentity(string.Empty, useSteamName: true, colorIndex: 0, cursorSkinId: string.Empty);

        public ProfileSummary? CreateProfile(SlotCreationRequest request = default) => null;

        public bool SaveIdentity(in ProfileIdentity identity)
        {
            Identity = identity;
            return true;
        }

        /// <summary>Заглушка: статистика профиля в этих тестах не проверяется.</summary>
        public void AddPlayedTime(long seconds) { }

        /// <summary>Заглушка: итоги забегов в этих тестах не проверяются.</summary>
        public void RecordRunFinished(bool victory, int nodesPassed) { }

        public bool SelectProfile(string profileId) => false;
        public bool DeleteProfile(string profileId) => false;
        public ProfileSummary? CreateGuild(string name, SlotCreationRequest request = default) => null;
        public bool SelectGuild(string guildId) => false;
        public bool DeleteGuild(string guildId) => false;

        public event Action Changed { add { } remove { } }
    }
}
