using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.UI;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Чтение домов для экрана «Создать игру» не должно менять игроку активный дом.
    /// </summary>
    /// <remarks>
    /// Ловушка в устройстве службы профилей: ключ забега строится только для АКТИВНОЙ гильдии,
    /// поэтому «идёт ли там забег» приходится спрашивать, делая каждый дом активным по очереди.
    /// Забытое переключение обратно означало бы, что простой заход в меню молча переселил игрока в
    /// последний дом списка, — и заметил бы он это, только начав играть не тем составом.
    /// Инвариант живёт между вью и службой профилей, поэтому держится тестом, а не комментарием.
    /// </remarks>
    public sealed class NewGameGuildListTests
    {
        [Test]
        public void ReadGuilds_RestoresActiveGuild()
        {
            var profiles = new FakeProfiles("g2", "g1", "g2", "g3");

            NewGameScreenView.ReadGuilds(profiles, save: null);

            Assert.AreEqual("g2", profiles.ActiveGuild.Id,
                "просмотр списка домов сменил активный дом — забег уехал бы в чужой слот сохранения");
        }

        [Test]
        public void ReadGuilds_ReturnsEveryGuild()
        {
            var profiles = new FakeProfiles("g1", "g1", "g2");

            List<NewGameScreenView.GuildEntry> guilds = NewGameScreenView.ReadGuilds(profiles, save: null);

            Assert.AreEqual(2, guilds.Count);
            Assert.AreEqual("g1", guilds[0].Id);
            Assert.AreEqual("g2", guilds[1].Id);
        }

        [Test]
        public void ReadGuilds_WithoutProfiles_IsEmpty()
        {
            Assert.IsEmpty(NewGameScreenView.ReadGuilds(null, save: null));
        }

        /// <summary>Служба профилей без диска: помнит только, какой дом сейчас активен.</summary>
        private sealed class FakeProfiles : IProfileService
        {
            private readonly List<ProfileSummary> _guilds = new();
            private string _active;

            public FakeProfiles(string active, params string[] guildIds)
            {
                _active = active;
                foreach (string id in guildIds) _guilds.Add(new ProfileSummary(id, id));
            }

            public IReadOnlyList<ProfileSummary> Profiles => Array.Empty<ProfileSummary>();
            public ProfileSummary ActiveProfile => new("p1", "p1");
            public IReadOnlyList<ProfileSummary> Guilds => _guilds;
            public ProfileSummary ActiveGuild => _guilds.Find(g => g.Id == _active);
            public bool ProfilesFull => false;
            public bool GuildsFull => false;
            public string RunKey => $"profiles/p1/guilds/{_active}/run";

            public ProfileSummary? CreateProfile(string name) => null;
            public bool SelectProfile(string profileId) => false;
            public bool DeleteProfile(string profileId) => false;
            public ProfileSummary? CreateGuild(string name) => null;

            public bool SelectGuild(string guildId)
            {
                if (_guilds.FindIndex(g => g.Id == guildId) < 0) return false;
                _active = guildId;
                return true;
            }

            public bool DeleteGuild(string guildId) => false;

            public event Action Changed { add { } remove { } }
        }
    }
}
