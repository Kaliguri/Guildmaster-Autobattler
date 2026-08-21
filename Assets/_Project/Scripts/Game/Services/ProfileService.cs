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

        /// <summary>
        /// Все профили сводками. У АКТИВНОГО статистика пересчитывается на месте.
        /// </summary>
        /// <remarks>
        /// Список собран чтением диска, а правда активного профиля живёт в памяти: наигранное копится
        /// там и попадает в файл раз в минуту. Без пересчёта экран показывал бы состояние на момент
        /// запуска игры — то есть ноль наигранного у того, кто играет второй час.
        /// </remarks>
        public IReadOnlyList<ProfileSummary> Profiles
        {
            get
            {
                SyncActiveSummary();
                return _profiles;
            }
        }

        public IReadOnlyList<ProfileSummary> Guilds   => _guilds;

        public ProfileSummary ActiveProfile => _activeProfile != null
            ? new ProfileSummary(_activeProfile.Id, _activeProfile.Name, StatsOf(_activeProfile))
            : default;

        public ProfileSummary ActiveGuild { get; private set; }

        public bool HasActiveProfile => _activeProfile != null;

        public ProfileIdentity Identity => _activeProfile == null
            ? new ProfileIdentity(string.Empty, useSteamName: true, colorIndex: 0, cursorSkinId: string.Empty)
            : new ProfileIdentity(_activeProfile.DisplayName, _activeProfile.UseSteamName,
                                  _activeProfile.ColorIndex, _activeProfile.CursorSkinId);

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
            if (_profiles.Count > 0) SelectProfile(_profiles[0].Id);

            // Профиль здесь БОЛЬШЕ НЕ СОЗДАЁТСЯ (решение Макса 03.08.2026): на чистой установке игрок
            // сам заводит его в свободном слоте, и первое, что он видит в игре, — этот выбор. Молчаливое
            // создание было честной страховкой («забегу некуда писаться»), но оно же и лишало игрока
            // единственного места, где профиль виден как сущность, а не как строчка в чужом списке.
        }

        // ── Профили ──────────────────────────────────────────────────────────

        public ProfileSummary? CreateProfile()
        {
            if (ProfilesFull)
            {
                Debug.LogWarning($"[ProfileService] - профилей уже {_profiles.Count} (лимит {_config.MaxProfiles})");
                return null;
            }

            var profile = new ProfileState
            {
                Id         = Guid.NewGuid().ToString("N"),
                Name       = NextSlotName(),
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
            return new ProfileSummary(profile.Id, profile.Name, StatsOf(profile));
        }

        public bool SaveIdentity(in ProfileIdentity identity)
        {
            if (_activeProfile == null) return false;

            _activeProfile.DisplayName  = identity.DisplayName;
            _activeProfile.UseSteamName = identity.UseSteamName;
            _activeProfile.ColorIndex   = identity.ColorIndex;
            _activeProfile.CursorSkinId = identity.CursorSkinId;

            _save.Save(ProfileKey(_activeProfile.Id), _activeProfile);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Имя для нового профиля: «Профиль N» с наименьшим свободным номером. Не по числу профилей —
        /// иначе, удалив первый из двух, игрок получил бы второй «Профиль 2» рядом с уже имеющимся.
        /// </summary>
        private string NextSlotName()
        {
            int limit = Math.Max(1, _config.MaxProfiles);
            for (int slot = 1; slot <= limit; slot++)
            {
                string candidate = $"Профиль {slot}";

                bool taken = false;
                for (int i = 0; i < _profiles.Count && !taken; i++) taken = _profiles[i].Name == candidate;

                if (!taken) return candidate;
            }

            return $"Профиль {_profiles.Count + 1}";
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

                // Удалили последний — профиля нет, и это законное состояние: игру встретит экран выбора.
                if (_profiles.Count > 0) SelectProfile(_profiles[0].Id);
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
                Id             = Guid.NewGuid().ToString("N"),
                Name           = string.IsNullOrWhiteSpace(name) ? "Гильдия" : name,
                CreatedUtc     = DateTime.UtcNow.ToString("o"),
                RosterCapacity = Math.Max(1, _config.StartingRosterCapacity),
            };

            _save.Save(GuildKey(_activeProfile.Id, guild.Id), guild);
            // Книга заводится сразу и пустой: дом без памяти невозможен, а отдельный ключ бережёт
            // экран казарм от чтения всей истории (реш. 2026-07-27/19).
            _save.Save(BookKey(_activeProfile.Id, guild.Id), new GuildBook());
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

        /// <summary>
        /// Прибавить наигранное и отметить дату. Пишем на диск не чаще раза в минуту.
        /// </summary>
        /// <remarks>
        /// Секунды копятся в самом состоянии профиля (оно в памяти), а файл трогается редко: писать
        /// сейв каждую секунду ради счётчика значит устраивать дисковую активность на ровном месте и
        /// ловить порчу файла на каждом падении. Всё несохранённое теряется только при аварийном
        /// завершении, и цена этого — минута наигранного.
        /// </remarks>
        public void AddPlayedTime(long seconds)
        {
            if (_activeProfile == null || seconds <= 0) return;

            _activeProfile.PlayedSeconds += seconds;
            _activeProfile.LastPlayedUtc = DateTime.UtcNow.ToString("O");

            _unsavedPlaySeconds += seconds;
            if (_unsavedPlaySeconds < PlayTimeFlushSeconds) return;

            _unsavedPlaySeconds = 0;
            _save.Save(ProfileKey(_activeProfile.Id), _activeProfile);
        }

        /// <summary>Отметить завершённый забег и обновить лучший результат.</summary>
        public void RecordRunFinished(bool victory, int nodesPassed)
        {
            if (_activeProfile == null) return;

            _activeProfile.RunsFinished++;
            if (victory) _activeProfile.RunsWon++;
            if (nodesPassed > _activeProfile.BestRunNodes) _activeProfile.BestRunNodes = nodesPassed;

            // Итог забега пишем сразу: это событие редкое и дорогое для игрока — потерять его
            // из-за отложенной записи значит обесценить час игры.
            _activeProfile.LastPlayedUtc = DateTime.UtcNow.ToString("O");
            _save.Save(ProfileKey(_activeProfile.Id), _activeProfile);
            Changed?.Invoke();
        }

        /// <summary>
        /// Собрать статистику профиля. Дома и открытия СЧИТАЮТСЯ, а не хранятся: у этих чисел уже
        /// есть владелец, и второй счётчик разошёлся бы с ним при первом удалении дома.
        /// </summary>
        private ProfileStats StatsOf(ProfileState profile)
        {
            if (profile == null) return default;

            int guilds = 0;
            foreach (string _ in _save.List(GuildsRoot(profile.Id))) guilds++;

            int unlocks = (profile.UnlockedPregenIds?.Count ?? 0)
                        + (profile.UnlockedFateIds?.Count ?? 0)
                        + (profile.UnlockedCaptainIds?.Count ?? 0)
                        + (profile.UnlockedDevNoteIds?.Count ?? 0);

            DateTime played = default;
            if (!string.IsNullOrEmpty(profile.LastPlayedUtc))
                DateTime.TryParse(profile.LastPlayedUtc, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.RoundtripKind, out played);

            return new ProfileStats(profile.PlayedSeconds, played, guilds,
                                    profile.RunsFinished, profile.RunsWon, profile.BestRunNodes, unlocks);
        }

        /// <summary>Сколько наигранного копим в памяти, прежде чем тронуть файл.</summary>
        private const long PlayTimeFlushSeconds = 60;

        private long _unsavedPlaySeconds;

        // ── Внутреннее ───────────────────────────────────────────────────────

        /// <summary>
        /// Освежить сводку активного профиля в списке: в памяти она новее, чем на диске.
        /// </summary>
        /// <remarks>
        /// Зовётся ленивно, из геттера списка, а не на каждое изменение: наигранное прибавляется раз в
        /// секунду, а <see cref="StatsOf"/> перечисляет каталог домов — считать это ежесекундно значит
        /// дёргать диск ради числа, которое никто в этот момент не смотрит.
        /// </remarks>
        private void SyncActiveSummary()
        {
            if (_activeProfile == null) return;

            for (int i = 0; i < _profiles.Count; i++)
            {
                if (_profiles[i].Id != _activeProfile.Id) continue;

                _profiles[i] = new ProfileSummary(_activeProfile.Id, _activeProfile.Name,
                                                  StatsOf(_activeProfile));
                return;
            }
        }

        private void RefreshProfiles()
        {
            _profiles.Clear();
            foreach (string id in _save.List(ProfilesRoot))
            {
                SaveLoadResult<ProfileState> loaded = _save.TryLoad<ProfileState>(ProfileKey(id));
                if (loaded.IsOk) _profiles.Add(new ProfileSummary(loaded.Value.Id, loaded.Value.Name,
                                                                  StatsOf(loaded.Value)));
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
        private static string BookKey(string profileId, string guildId)     => $"{GuildFolder(profileId, guildId)}/book";

        /// <summary>Указатель на профиль, которым играли в прошлый раз. Отдельно от самих профилей.</summary>
        [Serializable]
        [SaveSchema(1)]
        private sealed class SessionPointer
        {
            public string LastProfileId = string.Empty;
        }
    }
}
