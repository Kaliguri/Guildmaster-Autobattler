using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Guildmaster.Tests.EditMode.Guild
{
    /// <summary>
    /// Стартовая гильдия + мост гильдия→ростер боя (уточн. Макса 2026-07-17): <see cref="RunStateService.NewDefaultRun"/>
    /// даёт 4 стандартных сосуда с базовым реликом; <see cref="GuildRoster.Resolve"/> резолвит слоты в
    /// <see cref="PlayerSlot"/> по контент-БД (пропуская несуществующие релики); фабрика транзиентного пресета
    /// собирает Free-бой из ростера. Так узел забега деплоит СВОЮ четвёрку, а не канон-ростер пресета.
    /// </summary>
    public sealed class GuildRosterTests
    {
        private GameConfig      _config;
        private RunStateService _runStates;

        [SetUp]
        public void SetUp()
        {
            _config    = ScriptableObject.CreateInstance<GameConfig>();
            _runStates = new RunStateService(new MemSave(), _config);
        }

        [Test]
        public void NewDefaultRun_BuildsStandardGuild()
        {
            RunState run = _runStates.NewDefaultRun(1L);

            Assert.AreEqual(_config.GuildSize, run.Guild.Length, "Гильдия = GuildSize сосудов.");
            Assert.AreEqual(4, run.Guild.Length, "Дефолт GDD = 4.");
            foreach (RosterSlot s in run.Guild)
            {
                Assert.AreEqual("relic.base", s.RelicId, "Стартовый сосуд несёт базовый релик (пустой кит).");
                Assert.IsEmpty(s.VesselId, "Сосуд-контента пока нет → VesselId пуст.");
            }

            // Позиции различны (колонка), не слиплись в одну точку.
            var ys = new HashSet<float>();
            foreach (RosterSlot s in run.Guild) ys.Add(s.SavedPosition.y);
            Assert.AreEqual(run.Guild.Length, ys.Count, "У каждого сосуда своя стартовая позиция.");
        }

        [Test]
        public void Resolve_MapsGuildToRoster_ByContentId()
        {
            var content = new FakeContent();
            content.Add(MakeRelic("relic.base"));
            content.Add(MakeRelic("relic.druid"));

            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].RelicId = "relic.druid"; // навесили собранный релик на первый сосуд

            PlayerSlot[] roster = GuildRoster.Resolve(run, content);

            Assert.AreEqual(4, roster.Length);
            Assert.AreEqual("relic.druid", roster[0].Relic.Id);
            Assert.AreEqual("relic.base",  roster[1].Relic.Id);
        }

        [Test]
        public void Resolve_FallsBackToBaseKit_WhenRelicMissing()
        {
            var content = new FakeContent();
            content.Add(MakeRelic("relic.base"));

            RunState run = _runStates.NewDefaultRun(1L);
            run.Guild[0].RelicId = "relic.ghost"; // нет в БД → сосуд встаёт с базовым китом (с варнингом)

            LogAssert.Expect(LogType.Warning, new Regex("relic\\.ghost"));
            PlayerSlot[] roster = GuildRoster.Resolve(run, content);

            // Ростер идёт слот-в-слот с гильдией: по индексу расстановка пишет обратно позиции и киты,
            // поэтому «плохой» сосуд не выпадает, а откатывается на базовый кит.
            Assert.AreEqual(4, roster.Length, "Слот не должен выпадать — индексы гильдии обязаны совпадать.");
            Assert.AreEqual("relic.base", roster[0].Relic.Id);
        }

        [Test]
        public void Resolve_EmptyGuild_ReturnsEmpty()
        {
            _runStates.NewRun(1L, Array.Empty<RosterSlot>());
            Assert.AreEqual(0, GuildRoster.Resolve(_runStates.Current, new FakeContent()).Length);
        }

        [Test]
        public void CreateRuntime_BuildsFreePreset_FromRoster()
        {
            var roster = new[] { new PlayerSlot(MakeRelic("relic.base"), null, new Vector2(-6f, 0f)) };
            BattlePresetData preset = BattlePresetData.CreateRuntime(
                encounter: null, roster: roster, mode: DeploymentMode.Free, partyItems: null,
                isElite: true, id: "battle.run.x");

            Assert.AreEqual(DeploymentMode.Free, preset.DeploymentMode, "Забег форсит расстановку.");
            Assert.AreEqual(1, preset.Roster.Count);
            Assert.AreEqual("relic.base", preset.Roster[0].Relic.Id);
            Assert.IsTrue(preset.IsElite, "Элитность прокидывается из авторского пресета.");
            Assert.AreEqual("battle.run.x", preset.Id);
        }

        // ── helpers ──────────────────────────────────────────────
        private static RelicData MakeRelic(string id)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            typeof(ContentDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(r, id);
            return r;
        }

        private sealed class FakeContent : IContentDatabase
        {
            private readonly Dictionary<string, ContentDefinition> _byId = new();
            public void Add(ContentDefinition d) => _byId[d.Id] = d;
            public bool TryGet<T>(string id, out T def) where T : ContentDefinition
            {
                if (_byId.TryGetValue(id, out var d) && d is T t) { def = t; return true; }
                def = null; return false;
            }
            public IReadOnlyList<T> All<T>() where T : ContentDefinition => Array.Empty<T>();
        }

        private sealed class MemSave : ISaveService
        {
            private readonly Dictionary<string, object> _s = new();
            public void Save<T>(string key, T value) => _s[key] = value;
            public T Load<T>(string key) => _s.TryGetValue(key, out var v) ? (T)v : default;
            public bool Exists(string key) => _s.ContainsKey(key);
            public void Delete(string key) => _s.Remove(key);
        }
    }
}
