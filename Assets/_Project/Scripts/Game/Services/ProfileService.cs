using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реализация <see cref="IProfileService"/> поверх <see cref="ISaveService"/>. Дерево на диске:
    /// <c>profiles/{profileId}/profile</c> и <c>profiles/{profileId}/guilds/{guildId}/{guild,run}</c>.
    /// <para><b>Список профилей не дублируется индекс-файлом:</b> он читается из самого дерева
    /// (<see cref="ISaveService.List"/>). Индекс был бы вторым владельцем того же факта и разъехался бы
    /// с деревом на первом же сбое записи.</para>
    /// <para><b>Указатель на активный профиль</b> лежит отдельным файлом <c>session</c>, а активная
    /// гильдия — полем внутри профиля: так «продолжить» помнит, где играли, и это переезжает между
    /// машинами вместе с сейвами.</para>
    /// <para>Entry point: на старте поднимает прошлый выбор, а на чистой установке заводит первый
    /// профиль с первой гильдией — иначе забегу физически некуда писаться. Это не тихая подстановка:
    /// созданное сразу видно игроку в списке, и он волен переименовать или удалить.</para>
    /// </summary>
    public sealed class ProfileService : IProfileService, IStartable
    {
        private const string ProfilesRoot = "profiles";
        private const string SessionKey   = "session";

        private readonly ISaveService _save;
        private readonly GameConfig   _config;

        private readonly List<ProfileSummary> _profiles = new();
        private readonly List<ProfileSummary> _guilds   = new();

        private ProfileState _activeProfile;

        public ProfileService(ISaveService save, GameConfig config)
        {
            _save   = save;
            _config = config;
        }

        public IReadOnlyList<ProfileSummary> Profiles => _profiles;
        public IReadOnlyList<ProfileSummary> Guilds   => _guilds;

        public ProfileSummary ActiveProfile => _activeProfile != null
            ? new ProfileSummary(_activeProfile.Id, _activeProfile.Name)
            : default;

        public ProfileSummary ActiveGuild { get; private set; }

        public bool ProfilesFull => _profiles.Count >= Math.Max(1, _config.MaxProfiles);
        public bool GuildsFull   => _guilds.Count   >= Math.Max(1, _config.MaxGuildsPerProfile);

        public string RunKey => _activeProfile == null || string.IsNullOrEmpty(ActiveGuild.Id)
            ? string.Empty
            : GuildFolder(_activeProfile.Id, ActiveGuild.Id) + "/run";

        public event Action Changed;

        void IStartable.Start() => Initialize();

        /// <summary>
        /// Поднять прошлый выбор игрока, а на чистой установке завести первый профиль с гильдией.
        /// Публичный (а не только через <see cref="IStartable"/>), чтобы вызываться напрямую — из тестов,
        /// которым VContainer не нужен, и из будущего экрана выбора профиля.
        /// </summary>
        public void Initialize()
        {
            RefreshProfiles();

            string lastProfileId = _save.TryLoad<SessionPointer>(SessionKey) is { IsOk: true } s
                ? s.Value.LastProfileId
                : string.Empty;

            if (!string.IsNullOrEmpty(lastProfileId) && SelectProfile(lastProfileId)) return;
            if (_profiles.Count > 0 && SelectProfile(_profiles[0].Id)) return;

            // Чистая установка: без профиля и гильдии забегу некуда писаться.
            CreateProfile("Профиль 1");
        }

        // ── Профили ──────────────────────────────────────────────────────────

        public ProfileSummary? CreateProfile(string name)
        {
            if (ProfilesFull)
            {
                Debug.LogWarning($"[ProfileService] - профилей уже {_profiles.Count} (лимит {_config.MaxProfiles})");
                return null;
            }

            var profile = new ProfileState
            {
                Id         = Guid.NewGuid().ToString("N"),
                Name       = string.IsNullOrWhiteSpace(name) ? "Профиль" : name,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            };

            _save.Save(ProfileKey(profile.Id), profile);
            RefreshProfiles();

            _activeProfile = profile;
            RememberActiveProfile();
            RefreshGuilds();

            // Новый профиль без дома бесполезен: играть некуда, а экран выбора показал бы пустоту.
            if (_guilds.Count == 0) CreateGuild("Гильдия 1");

            Changed?.Invoke();
            return new ProfileSummary(profile.Id, profile.Name);
        }

        public bool SelectProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return false;

            SaveLoadResult<ProfileState> loaded = _save.TryLoad<ProfileState>(ProfileKey(profileId));
            if (!loaded.IsOk) return false;

            _activeProfile = loaded.Value;
            RememberActiveProfile();
            RefreshGuilds();

            // Профиль помнит, где играли; если та гильдия исчезла — берём первую, а не оставляем пустоту.
            if (!SelectGuild(_activeProfile.LastGuildId) && _guilds.Count > 0)
                SelectGuild(_guilds[0].Id);

            Changed?.Invoke();
            return true;
        }

        public bool DeleteProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return false;
            if (!_save.Exists(ProfileKey(profileId))) return false;

            _save.DeleteTree(ProfileFolder(profileId)); // вместе с гильдиями и их забегами
            RefreshProfiles();

            if (_activeProfile != null && _activeProfile.Id == profileId)
            {
                _activeProfile = null;
                ActiveGuild = default;
                _guilds.Clear();

                if (_profiles.Count > 0) SelectProfile(_profiles[0].Id);
                else                     CreateProfile("Профиль 1"); // без профиля игра неработоспособна
            }

            Changed?.Invoke();
            return true;
        }

        // ── Гильдии ──────────────────────────────────────────────────────────

        public ProfileSummary? CreateGuild(string name)
        {
            if (_activeProfile == null) return null;
            if (GuildsFull)
            {
                Debug.LogWarning($"[ProfileService] - гильдий уже {_guilds.Count} (лимит {_config.MaxGuildsPerProfile})");
                return null;
            }

            var guild = new GuildState
            {
                Id         = Guid.NewGuid().ToString("N"),
                Name       = string.IsNullOrWhiteSpace(name) ? "Гильдия" : name,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            };

            _save.Save(GuildKey(_activeProfile.Id, guild.Id), guild);
            RefreshGuilds();
            SetActiveGuild(guild.Id, guild.Name);

            Changed?.Invoke();
            return new ProfileSummary(guild.Id, guild.Name);
        }

        public bool SelectGuild(string guildId)
        {
            if (_activeProfile == null || string.IsNullOrEmpty(guildId)) return false;

            SaveLoadResult<GuildState> loaded = _save.TryLoad<GuildState>(GuildKey(_activeProfile.Id, guildId));
            if (!loaded.IsOk) return false;

            SetActiveGuild(loaded.Value.Id, loaded.Value.Name);
            Changed?.Invoke();
            return true;
        }

        public bool DeleteGuild(string guildId)
        {
            if (_activeProfile == null || string.IsNullOrEmpty(guildId)) return false;
            if (!_save.Exists(GuildKey(_activeProfile.Id, guildId))) return false;

            _save.DeleteTree(GuildFolder(_activeProfile.Id, guildId)); // вместе с забегом
            RefreshGuilds();

            if (ActiveGuild.Id == guildId)
            {
                ActiveGuild = default;
                if (_guilds.Count > 0) SelectGuild(_guilds[0].Id);
                else                   CreateGuild("Гильдия 1");
            }

            Changed?.Invoke();
            return true;
        }

        // ── Внутреннее ───────────────────────────────────────────────────────

        private void RefreshProfiles()
        {
            _profiles.Clear();
            foreach (string id in _save.List(ProfilesRoot))
            {
                SaveLoadResult<ProfileState> loaded = _save.TryLoad<ProfileState>(ProfileKey(id));
                if (loaded.IsOk) _profiles.Add(new ProfileSummary(loaded.Value.Id, loaded.Value.Name));
            }
        }

        private void RefreshGuilds()
        {
            _guilds.Clear();
            if (_activeProfile == null) return;

            foreach (string id in _save.List(GuildsRoot(_activeProfile.Id)))
            {
                SaveLoadResult<GuildState> loaded = _save.TryLoad<GuildState>(GuildKey(_activeProfile.Id, id));
                if (loaded.IsOk) _guilds.Add(new ProfileSummary(loaded.Value.Id, loaded.Value.Name));
            }
        }

        private void SetActiveGuild(string id, string name)
        {
            ActiveGuild = new ProfileSummary(id, name);
            if (_activeProfile == null || _activeProfile.LastGuildId == id) return;

            _activeProfile.LastGuildId = id;
            _save.Save(ProfileKey(_activeProfile.Id), _activeProfile);
        }

        private void RememberActiveProfile() =>
            _save.Save(SessionKey, new SessionPointer { LastProfileId = _activeProfile?.Id ?? string.Empty });

        private static string ProfileFolder(string profileId) => $"{ProfilesRoot}/{profileId}";
        private static string ProfileKey(string profileId)    => $"{ProfileFolder(profileId)}/profile";
        private static string GuildsRoot(string profileId)    => $"{ProfileFolder(profileId)}/guilds";

        private static string GuildFolder(string profileId, string guildId) => $"{GuildsRoot(profileId)}/{guildId}";
        private static string GuildKey(string profileId, string guildId)    => $"{GuildFolder(profileId, guildId)}/guild";

        /// <summary>Указатель на профиль, которым играли в прошлый раз. Отдельно от самих профилей.</summary>
        [Serializable]
        [SaveSchema(1)]
        private sealed class SessionPointer
        {
            public string LastProfileId = string.Empty;
        }
    }
}
