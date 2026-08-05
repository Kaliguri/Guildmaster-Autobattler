using System;
using System.Collections;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Session;
using Guildmaster.Net;
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
    /// Чего напарник НЕ может: двигать сторону, которой не распоряжается, и трогать арену, когда
    /// расстановки нет.
    /// </summary>
    /// <remarks>
    /// <b>Ради этого и заводилась перепроверка у хозяина.</b> Руки игрока живут у обоих участников и
    /// проверяют зону сами — но руки бывают чужие и бывают устаревшие, поэтому последнее слово за тем,
    /// кто владеет ареной. Проверка, которую никто не проверяет, имеет обыкновение тихо исчезать при
    /// следующем рефакторинге.
    ///
    /// <para><b>Отказ здесь молчаливый, и это правильно:</b> у автора намерения боец просто остаётся
    /// на месте, а звук отказа ему уже сыграли собственные руки. Поэтому тест смотрит не на ответ, а
    /// на ПОЗИЦИЮ — единственное, что вообще должно было измениться.</para>
    /// </remarks>
    public sealed class CoopIntentRightsTest
    {
        private const float BootTimeout = 20f;

        /// <summary>Номер игрока, которого нет в составе сеанса, — чужие руки в чистом виде.</summary>
        private const int StrangerPlayerId = 42;

        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator MovingOnOurOwnGround_IsApplied()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            INetTransport partner = root.Container.Resolve<LoopbackNetwork>().CreateNode();

            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);

            // Обычное Ристалище: обе стороны наши, право заведомо есть — проверяется САМ ПУТЬ, а не
            // фильтр. Без этого сценария все проверки намерений были бы про отказ, и «принято» держал
            // бы только один тест — на локальный клик, который по сети не ходит.
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(8);

            IArenaUnits arena = FindAnywhere<IArenaUnits>();
            Assert.IsNotNull(arena, "у хозяина нет шва «кто на арене»");
            Assert.IsNotEmpty(arena.Units, "площадка пуста — двигать некого");

            ArenaUnit unit  = arena.Units[0];
            Vector2 before  = unit.Position;
            var target      = new Vector2(before.x, before.y + 0.5f); // тот же участок зоны, соседи по X

            SendMoveIntent(partner, unit.Id, target, StrangerPlayerId);

            yield return WaitUntil(() => arena.TryGet(unit.Id, out ArenaUnit u) && u.Position != before,
                                   partner, seconds: 5f);

            Assert.IsTrue(arena.TryGet(unit.Id, out ArenaUnit after), "боец исчез с арены");
            Assert.AreNotEqual(before, after.Position,
                "намерение по сети не применилось — у гостя перетаскивание не будет работать вовсе");
            Assert.AreEqual(target, after.Position,
                "боец переехал НЕ ТУДА, куда его звали — хозяин применил намерение по-своему");

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        [UnityTest]
        public IEnumerator MovingSomeoneElsesSide_IsRefused()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            INetTransport partner = root.Container.Resolve<LoopbackNetwork>().CreateNode();

            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);

            // Место, где стороны РАЗДЕЛЕНЫ: на обычном Ристалище обе наши, и правило там не проверить.
            activities.Open(new ActivitySetup(ActivityKind.ProvingGrounds, ownUnitsOnly: true));
            yield return WaitFrames(8);

            IArenaUnits arena = FindAnywhere<IArenaUnits>();
            Assert.IsNotNull(arena, "у хозяина нет шва «кто на арене» — двигать некого");

            ArenaUnit theirs = default;
            yield return WaitUntil(() => TryFindEnemy(arena, out theirs), partner, seconds: 5f);
            Assert.AreNotEqual(0, theirs.Id, "на площадке нет чужой стороны — проверять нечего");

            Vector2 before = theirs.Position;
            SendMoveIntent(partner, theirs.Id, before + new Vector2(1.5f, 0f), StrangerPlayerId);

            yield return WaitFrames(20);

            Assert.IsTrue(arena.TryGet(theirs.Id, out ArenaUnit after), "боец исчез с арены");
            Assert.AreEqual(before, after.Position,
                "чужие руки подвинули бойца стороны, которой не распоряжаются — хозяин не проверил право");

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        [UnityTest]
        public IEnumerator MovingAfterTheBattleStarted_IsRefused()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadGame();

            var root = UnityEngine.Object.FindAnyObjectByType<RootLifetimeScope>();
            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            INetTransport partner = root.Container.Resolve<LoopbackNetwork>().CreateNode();

            sessions.Open(SessionRole.Owner);
            yield return WaitFrames(1);
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(8);

            IArenaUnits arena = FindAnywhere<IArenaUnits>();
            Assert.IsNotNull(arena, "у хозяина нет шва «кто на арене»");
            Assert.IsNotEmpty(arena.Units, "площадка пуста — двигать некого");
            int unitId = arena.Units[0].Id;

            // Бой начался — расстановка кончилась. Намерение приходит с опозданием: так и бывает в
            // сети, где чужая арена отстаёт от нашей на задержку.
            var gate = FindAnywhere<Guildmaster.Core.Net.IReadyGate>();
            Assert.IsNotNull(gate, "нет общего согласия — бой не начать");
            gate.ToggleLocal();
            yield return WaitFrames(10);

            // Точка заведомо далёкая и заведомо вне зоны расстановки. Проверять «позиция не
            // изменилась» в бою нельзя — бойцы там двигаются сами; проверяем, что боец не ОКАЗАЛСЯ
            // там, куда его звали.
            var faraway = new Vector2(999f, 999f);
            SendMoveIntent(partner, unitId, faraway, StrangerPlayerId);
            yield return WaitFrames(20);

            if (arena.TryGet(unitId, out ArenaUnit after))
            {
                Assert.Less((after.Position - faraway).magnitude, 1e9f, "боец на месте — сравниваем осмысленно");
                Assert.Greater((after.Position - faraway).magnitude, 1f,
                    "боец прыгнул туда, куда его звали посреди боя — расстановка приняла опоздавшее намерение");
            }

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        // ── помощники ────────────────────────────────────────────────────────

        private static bool TryFindEnemy(IArenaUnits arena, out ArenaUnit unit)
        {
            IReadOnlyList<ArenaUnit> units = arena.Units;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Team == 0 || units[i].IsDead) continue;
                unit = units[i];
                return true;
            }

            unit = default;
            return false;
        }

        /// <summary>
        /// «Поставь бойца сюда» на проводе: id, точка и автор. Формат собираем руками — кодек живёт в
        /// игровой сборке как деталь моста, и открывать его ради теста не за что.
        /// </summary>
        private static void SendMoveIntent(INetTransport partner, int unitId, Vector2 position, int playerId)
        {
            var writer = new NetByteWriter(32);
            writer.WriteInt(unitId);
            writer.WriteFloat(position.x);
            writer.WriteFloat(position.y);
            writer.WriteInt(playerId);

            byte[] envelope = null;
            partner.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.DeploymentIntent, writer.WrittenSegment, ref envelope),
                NetDelivery.Reliable);
        }

        private static T FindAnywhere<T>() where T : class
        {
            LifetimeScope[] scopes = UnityEngine.Object.FindObjectsByType<LifetimeScope>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < scopes.Length; i++)
            {
                if (scopes[i] == null || scopes[i].Container == null) continue;
                if (scopes[i].Container.TryResolve(out T value)) return value;
            }
            return null;
        }

        private static IEnumerator WaitUntil(Func<bool> done, INetTransport partner, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) yield break; // приговор выносит Assert
                partner.Poll();
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
