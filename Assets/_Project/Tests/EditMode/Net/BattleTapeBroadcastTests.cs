using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Net.Tape;
using Guildmaster.Net.Transport;
using Guildmaster.Tests.EditMode.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Провод между живым боем и раздачей ленты: что именно уезжает гостям и когда.
    /// <para>Здесь проверяются три правила, каждое из которых при нарушении ломается тихо: раздаётся
    /// только <b>досчитанный</b> тик (иначе в чанк уедет тик без кадра), хвост боя дожимается
    /// <b>один раз</b> (иначе после конца боя в сеть капает пустота каждый кадр), и dev-рестарт
    /// <b>сбрасывает нумерацию</b> (иначе гость примет чанки нового боя за дубли и не увидит его вовсе).</para>
    /// </summary>
    public sealed class BattleTapeBroadcastTests
    {
        [Test]
        public void OnlyCompletedTicks_AreHandedOut()
        {
            Fixture f = Build();

            // 31 тик: досчитаны 0..30, значит готов ровно один чанк из тридцати (0..29).
            TickSim(f, 31);
            f.Broadcast.Tick();

            Assert.AreEqual(1, f.Streamer.SentChunkCount, "Уехал один полный чанк");
            Assert.AreEqual(30, f.Streamer.NextTick, "Тик 30 остался у хоста — он ещё не набрал чанк");
        }

        [Test]
        public void BeforeTheFirstTick_NothingIsSent()
        {
            Fixture f = Build();

            f.Broadcast.Tick();

            Assert.AreEqual(0, f.Streamer.SentChunkCount, "Досчитанных тиков нет — раздавать нечего");
        }

        [Test]
        public void ClosedGate_HoldsTheTape()
        {
            Fixture f = Build();
            f.Broadcast.Enabled = false;

            TickSim(f, 40);
            f.Broadcast.Tick();

            Assert.AreEqual(0, f.Streamer.SentChunkCount, "Пока раздача закрыта, лента не уходит фрагментом");
        }

        // Конец боя дожимается ровно один раз: арена живёт дальше (мир не выгружается), и Flush каждый
        // кадр слал бы пустые чанки до самого выхода из боя.
        [Test]
        public void EndOfBattle_FlushesTheTail_Once()
        {
            Fixture f = Build();

            TickSim(f, 40);
            f.Broadcast.Tick();
            int afterPump = f.Streamer.SentChunkCount;

            // Убиваем вторую команду: исход боя решает сим сам, на ближайшем тике.
            foreach (RuntimeUnit unit in f.Sim.Units)
                if (unit.Team == 1) unit.CurrentHP = 0f;

            TickSim(f, 2);
            Assert.AreNotEqual(BattleOutcome.Ongoing, f.Sim.Outcome, "Бой кончился");

            f.Broadcast.Tick();
            int afterFlush = f.Streamer.SentChunkCount;
            f.Broadcast.Tick();

            Assert.Greater(afterFlush, afterPump, "Хвост уехал");
            Assert.AreEqual(afterFlush, f.Streamer.SentChunkCount, "И повторно не отправляется");
        }

        [Test]
        public void BattleReset_StartsChunkNumbersOver()
        {
            Fixture f = Build();

            TickSim(f, 31);
            f.Broadcast.Tick();
            Assert.AreEqual(30, f.Streamer.NextTick);

            f.Sim.ResetBattle();

            Assert.AreEqual(0, f.Streamer.NextTick, "Новый бой раздаётся с нулевого тика");
            Assert.AreEqual(0, f.Streamer.SentChunkCount, "И со своей нумерации чанков");
        }

        // ── помощники ────────────────────────────────────────────────────────────

        private sealed class Fixture
        {
            public CombatSimulation   Sim;
            public BattleTapeRecorder Recorder;
            public TapeStreamer       Streamer;
            public BattleTapeBroadcast Broadcast;
        }

        private static Fixture Build()
        {
            var sim = new CombatSimulation(
                new XorShiftRng(7UL), CombatTestValues.ArmorK, new SpatialHash(CombatTestValues.CellSize),
                new BrainSystem(), new AbilitySystem(), new MovementSystem(), new AutoAttackSystem(),
                new ProjectileSystem(), new DeathSystem(), new EffectSystem(), new RegenSystem());

            sim.EnqueueUnitSpawn(Unit(team: 0, x: -3f));
            sim.EnqueueUnitSpawn(Unit(team: 1, x:  3f));

            var tape     = new BattleTape(windowTicks: 512);
            var recorder = new BattleTapeRecorder(sim, tape, abilities: null, effects: null);
            var streamer = new TapeStreamer(new LoopbackNetwork().CreateNode(), tape, ticksPerChunk: 30);

            return new Fixture
            {
                Sim       = sim,
                Recorder  = recorder,
                Streamer  = streamer,
                Broadcast = new BattleTapeBroadcast(sim, streamer, HostRole),
            };
        }

        // Раздача — обязанность хоста; роль здесь фиксирована, потому что этот класс тестов про
        // ПРАВИЛА раздачи, а не про то, кто раздаёт (это CoopBattleWiringTests).
        private static readonly Guildmaster.Core.Net.IBattleAuthority HostRole = new AlwaysHost();

        private sealed class AlwaysHost : Guildmaster.Core.Net.IBattleAuthority
        {
            public Guildmaster.Core.Net.BattleRole Role => Guildmaster.Core.Net.BattleRole.Host;
            public bool SimulatesLocally => true;
        }

        // Тикаем так же, как боевой цикл: кадр ленты снимается сразу за тиком — иначе у чанка не будет
        // кадров, и тест проверял бы не то, что происходит в игре.
        private static void TickSim(Fixture f, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                f.Sim.Tick(SimConstants.TickDelta);
                f.Recorder.CaptureCurrentState();
            }
        }

        private static RuntimeUnit Unit(int team, float x)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("base", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, 500f),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat,  20f),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat,   1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat,   1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat,   3f),
            });

            return new RuntimeUnit
            {
                Team                 = team,
                Stats                = stats,
                CurrentHP            = 500f,
                Position             = new Vector2(x, 0f),
                PreviousPosition     = new Vector2(x, 0f),
                AutoAttackDamageType = DamageType.Slash,
            };
        }
    }
}
