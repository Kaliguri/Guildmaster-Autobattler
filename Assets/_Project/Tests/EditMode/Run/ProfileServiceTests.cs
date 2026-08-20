using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Run
{
    /// <summary>
    /// Иерархия сохранений: профиль → гильдии → забег (ТЗ [[save-system]] §3). Проверяется на настоящей
    /// реализации поверх памяти — важна не запись на диск, а правила: лимиты, переключение, что уносит
    /// удаление и куда указывает ключ забега.
    /// </summary>
    public sealed class ProfileServiceTests
    {
        private InMemorySaveService _save;
        private GameConfig          _config;
        private ProfileService      _profiles;

        [SetUp]
        public void SetUp()
        {
            _save   = new InMemorySaveService();
            _config = GameConfig.CreateDefault(); // заготовка: 4 профиля, 8 гильдий
            _profiles = new ProfileService(_save, _config);
            _profiles.Initialize();
        }

        /// <summary>
        /// Завести профиль так, как это делает игрок на экране слотов. Отдельным шагом, а не в
        /// <c>SetUp</c>: с 03.08.2026 игра профиль сама не создаёт, и «чистая установка» — законное
        /// состояние, которое проверяет отдельный тест.
        /// </summary>
        private void GivenProfile() => Assert.IsNotNull(_profiles.CreateProfile(), "профиль не завёлся");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void FirstLaunch_HasNoProfile_AndSaysSo()
        {
            // Игра больше НЕ заводит профиль молча (решение Макса 03.08.2026): первое, что видит игрок, —
            // выбор слота. Отсюда и требование к состоянию: пусто и честно об этом сообщает.
            Assert.IsFalse(_profiles.HasActiveProfile, "на чистой установке профиля быть не должно");
            Assert.IsEmpty(_profiles.Profiles, "и списка тоже");
            Assert.IsEmpty(_profiles.RunKey, "писать забег некуда, и это видно вызывающему");
        }

        [Test]
        public void CreatingAProfile_GivesItASlotNumberAndAHome()
        {
            GivenProfile();

            Assert.AreEqual("Профиль 1", _profiles.ActiveProfile.Name, "имя профиля — номер слота");
            Assert.AreEqual(1, _profiles.Guilds.Count, "новый профиль получает дом: без дома играть негде");
            Assert.IsNotEmpty(_profiles.RunKey, "и забегу теперь есть куда писаться");
        }

        [Test]
        public void SlotNumber_FillsTheFirstFreeSeat()
        {
            GivenProfile();
            GivenProfile();
            string second = _profiles.ActiveProfile.Id;

            _profiles.DeleteProfile(second);
            GivenProfile();

            Assert.AreEqual("Профиль 2", _profiles.ActiveProfile.Name,
                "номер берётся свободный, а не «сколько профилей» — иначе рядом встали бы два «Профиль 2»");
        }

        [Test]
        public void Identity_SurvivesProfileSwitching()
        {
            GivenProfile();
            string first = _profiles.ActiveProfile.Id;
            _profiles.SaveIdentity(new ProfileIdentity("Гроза", useSteamName: false, colorIndex: 2, cursorSkinId: "cursor.toon"));

            GivenProfile(); // второй слот со своей идентичностью
            Assert.IsTrue(_profiles.Identity.UseSteamName, "новый профиль начинает с имени из Steam");

            _profiles.SelectProfile(first);

            Assert.AreEqual("Гроза", _profiles.Identity.DisplayName);
            Assert.AreEqual(2, _profiles.Identity.ColorIndex);
            Assert.AreEqual("cursor.toon", _profiles.Identity.CursorSkinId);
            Assert.AreEqual("Гроза", _profiles.Identity.ResolveName("SteamNick"),
                "выбран свой ник — он и играет");
        }

        [Test]
        public void RunKey_PointsIntoTheActiveGuild()
        {
            GivenProfile();
            string key = _profiles.RunKey;

            StringAssert.StartsWith($"profiles/{_profiles.ActiveProfile.Id}/guilds/{_profiles.ActiveGuild.Id}", key);
            StringAssert.EndsWith("/run", key);
        }

        [Test]
        public void EachGuildIsItsOwnSaveSlot()
        {
            GivenProfile();
            string first = _profiles.RunKey;
            _profiles.CreateGuild("Второй дом");

            Assert.AreNotEqual(first, _profiles.RunKey,
                "гильдия и есть слот сохранения — у второго дома обязан быть свой забег");
        }

        [Test]
        public void Profiles_RespectTheConfiguredLimit()
        {
            while (!_profiles.ProfilesFull) Assert.IsNotNull(_profiles.CreateProfile());

            Assert.AreEqual(_config.MaxProfiles, _profiles.Profiles.Count);
            Assert.IsNull(_profiles.CreateProfile(), "лимит профилей не соблюдён");
        }

        [Test]
        public void Guilds_RespectTheConfiguredLimit()
        {
            GivenProfile();
            while (!_profiles.GuildsFull) Assert.IsNotNull(_profiles.CreateGuild("ещё дом"));

            Assert.AreEqual(_config.MaxGuildsPerProfile, _profiles.Guilds.Count);
            Assert.IsNull(_profiles.CreateGuild("сверх лимита"), "лимит гильдий не соблюдён");
        }

        [Test]
        public void SwitchingProfile_SwitchesTheGuildsWithIt()
        {
            GivenProfile();
            string firstProfile = _profiles.ActiveProfile.Id;
            _profiles.CreateGuild("Дом А");
            int guildsInFirst = _profiles.Guilds.Count;

            ProfileSummary? second = _profiles.CreateProfile();
            Assert.IsTrue(second.HasValue);
            Assert.AreEqual(1, _profiles.Guilds.Count, "у нового профиля свои дома, а не чужие");

            _profiles.SelectProfile(firstProfile);
            Assert.AreEqual(guildsInFirst, _profiles.Guilds.Count, "вернулись — дома на месте");
        }

        [Test]
        public void SwitchingProfile_RemembersWhereYouPlayed()
        {
            GivenProfile();
            string firstProfile = _profiles.ActiveProfile.Id;
            ProfileSummary? second = _profiles.CreateGuild("Дом Б");
            Assert.IsTrue(second.HasValue);
            string playedIn = _profiles.ActiveGuild.Id;

            _profiles.CreateProfile();
            _profiles.SelectProfile(firstProfile);

            Assert.AreEqual(playedIn, _profiles.ActiveGuild.Id,
                "профиль обязан помнить последний дом — иначе «Продолжить» открывает не тот забег");
        }

        [Test]
        public void DeletingAGuild_TakesItsRunWithIt()
        {
            GivenProfile();
            _profiles.CreateGuild("На снос");
            string doomedGuild = _profiles.ActiveGuild.Id;
            string runKey = _profiles.RunKey;
            _save.Save(runKey, new RunState { Gold = 777 });

            _profiles.DeleteGuild(doomedGuild);

            Assert.IsFalse(_save.Exists(runKey), "забег удалённой гильдии остался мусором на диске");
        }

        [Test]
        public void DeletingAProfile_TakesItsGuildsWithIt()
        {
            GivenProfile();
            ProfileSummary? doomed = _profiles.CreateProfile();
            Assert.IsTrue(doomed.HasValue);
            string guildKeyPrefix = $"profiles/{doomed.Value.Id}";
            string runKey = _profiles.RunKey;
            _save.Save(runKey, new RunState { Gold = 13 });

            _profiles.DeleteProfile(doomed.Value.Id);

            Assert.IsFalse(_save.Exists(runKey), "забеги удалённого профиля остались на диске");
            CollectionAssert.DoesNotContain(_save.List("profiles"), doomed.Value.Id);
            StringAssert.DoesNotContain(guildKeyPrefix, _profiles.RunKey);
        }

        [Test]
        public void DeletingTheLastProfile_LeavesTheGameWithoutOne()
        {
            GivenProfile();
            _profiles.DeleteProfile(_profiles.ActiveProfile.Id);

            // Профиль больше не воскресает сам: удалили последний — игру встретит экран выбора слота,
            // тот же, что на чистой установке.
            Assert.IsEmpty(_profiles.Profiles);
            Assert.IsFalse(_profiles.HasActiveProfile);
            Assert.IsEmpty(_profiles.RunKey, "писать забег снова некуда, и это видно");
        }

        [Test]
        public void RunService_RefusesToSaveWhenThereIsNoGuild()
        {
            var runs = new RunStateService(_save, _config, FixedProfileService.WithoutGuild());
            runs.NewDefaultRun(seed: 1);

            // Молча «сохранить в никуда» нельзя: игрок доиграл бы забег и потерял его целиком.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            runs.Autosave();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(runs.HasSave);
        }
    }
}
