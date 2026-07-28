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

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void FirstLaunch_CreatesAProfileWithAGuild_SoTheRunHasSomewhereToGo()
        {
            Assert.AreEqual(1, _profiles.Profiles.Count, "на чистой установке нужен профиль");
            Assert.AreEqual(1, _profiles.Guilds.Count, "и дом внутри него");
            Assert.IsNotEmpty(_profiles.RunKey, "иначе забегу физически некуда писаться");
        }

        [Test]
        public void RunKey_PointsIntoTheActiveGuild()
        {
            string key = _profiles.RunKey;

            StringAssert.StartsWith($"profiles/{_profiles.ActiveProfile.Id}/guilds/{_profiles.ActiveGuild.Id}", key);
            StringAssert.EndsWith("/run", key);
        }

        [Test]
        public void EachGuildIsItsOwnSaveSlot()
        {
            string first = _profiles.RunKey;
            _profiles.CreateGuild("Второй дом");

            Assert.AreNotEqual(first, _profiles.RunKey,
                "гильдия и есть слот сохранения — у второго дома обязан быть свой забег");
        }

        [Test]
        public void Profiles_RespectTheConfiguredLimit()
        {
            while (!_profiles.ProfilesFull) Assert.IsNotNull(_profiles.CreateProfile("ещё"));

            Assert.AreEqual(_config.MaxProfiles, _profiles.Profiles.Count);
            Assert.IsNull(_profiles.CreateProfile("сверх лимита"), "лимит профилей не соблюдён");
        }

        [Test]
        public void Guilds_RespectTheConfiguredLimit()
        {
            while (!_profiles.GuildsFull) Assert.IsNotNull(_profiles.CreateGuild("ещё дом"));

            Assert.AreEqual(_config.MaxGuildsPerProfile, _profiles.Guilds.Count);
            Assert.IsNull(_profiles.CreateGuild("сверх лимита"), "лимит гильдий не соблюдён");
        }

        [Test]
        public void SwitchingProfile_SwitchesTheGuildsWithIt()
        {
            string firstProfile = _profiles.ActiveProfile.Id;
            _profiles.CreateGuild("Дом А");
            int guildsInFirst = _profiles.Guilds.Count;

            ProfileSummary? second = _profiles.CreateProfile("Второй профиль");
            Assert.IsTrue(second.HasValue);
            Assert.AreEqual(1, _profiles.Guilds.Count, "у нового профиля свои дома, а не чужие");

            _profiles.SelectProfile(firstProfile);
            Assert.AreEqual(guildsInFirst, _profiles.Guilds.Count, "вернулись — дома на месте");
        }

        [Test]
        public void SwitchingProfile_RemembersWhereYouPlayed()
        {
            string firstProfile = _profiles.ActiveProfile.Id;
            ProfileSummary? second = _profiles.CreateGuild("Дом Б");
            Assert.IsTrue(second.HasValue);
            string playedIn = _profiles.ActiveGuild.Id;

            _profiles.CreateProfile("Другой");
            _profiles.SelectProfile(firstProfile);

            Assert.AreEqual(playedIn, _profiles.ActiveGuild.Id,
                "профиль обязан помнить последний дом — иначе «Продолжить» открывает не тот забег");
        }

        [Test]
        public void DeletingAGuild_TakesItsRunWithIt()
        {
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
            ProfileSummary? doomed = _profiles.CreateProfile("На снос");
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
        public void DeletingTheLastProfile_LeavesTheGamePlayable()
        {
            _profiles.DeleteProfile(_profiles.ActiveProfile.Id);

            Assert.AreEqual(1, _profiles.Profiles.Count, "без профиля игра неработоспособна — нужен новый");
            Assert.IsNotEmpty(_profiles.RunKey);
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
