using System;
using System.Collections;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Arena;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Session;
using Guildmaster.Game.Session.Net;
using Guildmaster.Net;
using Guildmaster.Net.Tape;
using Guildmaster.Net.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Гость видит на арене РОВНО ТЕХ, кого прислал хозяин, — главная жалоба ночного прогона.
    /// </summary>
    /// <remarks>
    /// «В бою видела союзников, не видела противников» (Макс, 05.08.2026). Союзники были видны потому,
    /// что отряд вне боя ставят тела мира; бойцы боя приезжают лентой, и её у гостя не было вовсе —
    /// боевой скоуп не поднимался. Скоуп починен, но проверки «в кадре ровно столько, сколько
    /// прислали» до сих пор не было ни одной.
    ///
    /// <para><b>Здесь тест не слушает провод, а КОРМИТ его:</b> собирает ленту тем же кодеком, что
    /// хозяин, и шлёт её гостю. Пассивный наблюдатель проверил бы только то, что случилось бы и без
    /// него, — этот урок уже стоил одного переписанного сценария.</para>
    ///
    /// <para><b>Кадров шлём несколько.</b> Показ у гостя идёт с отставанием и начинается не с первого
    /// же полученного тика: одиночный чанк доехал бы, но на экран не попал, и тест доказывал бы
    /// доставку вместо показа.</para>
    /// </remarks>
    public sealed class GuestSeesTheFightersTest
    {
        private const float BootTimeout = 20f;
        private const int   Fighters    = 2;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator Guest_SeesExactlyTheFightersTheHostSent()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            INetTransport host = root.Container.Resolve<LoopbackNetwork>().CreateNode(claimHost: true);

            var sessions = root.Container.Resolve<SessionHost>();
            sessions.Open(SessionRole.Guest);
            yield return WaitFrames(2);

            SendActivity(host, new ActivityState(
                ActivityKind.ProvingGrounds, hideOpponent: false, opposition: OpposingSide.Unclaimed,
                battleOpen: true, phase: BattlePhase.Deployment));

            LifetimeScope combat = null;
            yield return WaitUntil(() => (combat = LifetimeScope.Find<CombatLifetimeScope>()) != null
                                         && combat.Container != null,
                                   host, seconds: 5f);
            Assert.IsNotNull(combat?.Container, "боевой скоуп гостя не собрался");

            // Кто эти бойцы — отдельным сообщением: в снимках определения нет, оно за бой не меняется.
            for (int i = 0; i < Fighters; i++) SendPassport(host, unitId: i + 1, team: i);

            var arena = combat.Container.Resolve<IArenaUnits>();
            Assert.IsNotNull(arena, "у гостя нет шва «кто на арене»");

            // Кормим ленту: тик за тиком, как это делает хозяин по ходу боя.
            var tape   = new BattleTape(windowTicks: 128);
            var writer = new TapeChunkWriter();
            byte[] envelope = null;

            for (int tick = 0; tick < 60; tick++)
            {
                tape.CaptureSnapshots(tick, Frame(tick));

                if (writer.TryWrite(tape, tick, tickCount: 1, maxBytes: 64 * 1024,
                                    out ArraySegment<byte> chunk))
                {
                    host.SendToAll(NetEnvelope.Wrap(NetChannel.TapeChunk, chunk, ref envelope),
                                   NetDelivery.Reliable);
                }

                host.Poll();
                yield return null;

                if (arena.Units.Count == Fighters) break;
            }

            yield return WaitUntil(() => arena.Units.Count == Fighters, host, seconds: 5f);

            Assert.AreEqual(Fighters, arena.Units.Count,
                $"хозяин прислал {Fighters} бойцов, а гость видит {arena.Units.Count} — " +
                "ровно так и выглядит «подключился и никого не вижу»");

            // И это ОБЕ стороны, а не только своя: жалоба была именно про пропавших противников.
            Assert.IsTrue(arena.TryGet(1, out ArenaUnit ours) && ours.Team == 0, "своей стороны нет в кадре");
            Assert.IsTrue(arena.TryGet(2, out ArenaUnit theirs) && theirs.Team == 1, "чужой стороны нет в кадре");

            sessions.Close();
            yield return WaitFrames(2);
        }

        // ── что шлём ─────────────────────────────────────────────────────────

        /// <summary>Кадр боя: те же бойцы, слегка разъезжающиеся, — чтобы показ было что двигать.</summary>
        private static IReadOnlyList<UnitSnapshot> Frame(int tick)
        {
            var units = new List<UnitSnapshot>(Fighters);
            for (int i = 0; i < Fighters; i++)
            {
                var pos = new Vector2(-3f + i * 6f + tick * 0.01f, 0f);
                units.Add(new UnitSnapshot(
                    id: i + 1, team: i, position: pos, previousPosition: pos,
                    currentHp: 100f, maxHp: 100f, currentShield: 0f, currentResource: 0f, maxResource: 0f,
                    size: 1f, phase: AttackPhase.Idle, windupTicks: 0, windupRemaining: 0,
                    attackCooldownTicks: 0, targetId: -1, effectTagMask: default, isDead: false));
            }
            return units;
        }

        private static void SendPassport(INetTransport host, int unitId, int team)
        {
            var writer = new NetByteWriter(32);
            writer.WriteInt(unitId);
            writer.WriteByte((byte)team);
            writer.WriteString(null); // определения нет — законный случай, так спавнятся дев-болванчики

            byte[] envelope = null;
            host.SendToAll(NetEnvelope.Wrap(NetChannel.BattleRoster, writer.WrittenSegment, ref envelope),
                           NetDelivery.Reliable);
        }

        private static void SendActivity(INetTransport host, in ActivityState state)
        {
            var writer = new NetByteWriter(16);
            byte[] envelope = null;
            ArraySegment<byte> payload = ActivityStateCodec.Write(state, writer);
            host.SendToAll(NetEnvelope.Wrap(NetChannel.ActivityState, payload, ref envelope),
                           NetDelivery.Reliable);
        }

        // ── помощники ────────────────────────────────────────────────────────

        private static IEnumerator WaitUntil(Func<bool> done, INetTransport host, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
                host.Poll();
                yield return null;
            }
        }

        private static IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync("CoreScene", LoadSceneMode.Single);

            float deadline = Time.realtimeSinceStartup + BootTimeout;
            while (UnityEngine.Object.FindAnyObjectByType<WorldLifetimeScope>() == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"мир не поднялся за {BootTimeout} с — бут сломан");
                yield return null;
            }

            yield return WaitFrames(2);
        }

        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
    }
}
