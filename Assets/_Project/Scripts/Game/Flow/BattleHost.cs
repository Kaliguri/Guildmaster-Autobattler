using System;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Единственный владелец жизненного цикла боя: рождает боевой скоуп на входе в бой и хоронит его
    /// на выходе. Живёт в мире — потому что тот, кто создаёт бой, не может жить внутри него.
    /// </summary>
    /// <remarks>
    /// <b>Дверь одна.</b> Забег входит через <see cref="IBattleSession"/> (делегаты привязаны здесь),
    /// dev-консоль зовёт <see cref="Open"/> напрямую. До 02.08.2026 дверей было две: консоль ходила
    /// прямо в <c>EncounterLoader</c> живого скоупа, потому что скоуп был вечным и его достаточно было
    /// найти. Теперь скоупа между боями не существует, и «найти» его нельзя — значит и второй двери
    /// быть не может.
    /// <para><b>Сид приезжает при рождении, а не пересевом.</b> Пока скоуп жил вечно, его генератор
    /// приходилось пересевать перед каждым боем вручную, иначе весь забег тянул одну
    /// последовательность. Теперь генератор рождается вместе с боем и сразу с нужным сидом — пересев
    /// вырван с корнем, а не спрятан за вызовом.</para>
    /// </remarks>
    public sealed class BattleHost : IStartable, IDisposable
    {
        private readonly IBattleSession   _session;
        private readonly RunStateService  _runStates;
        private readonly LifetimeScope    _world;
        private readonly CombatLifetimeScope _battleScopePrefab;
        private readonly WorldStageController _worldStage;

        private CombatLifetimeScope _battle;
        private BattlePresetData    _lastPreset;

        public BattleHost(IBattleSession session, RunStateService runStates, LifetimeScope world,
                          CombatLifetimeScope battleScopePrefab, WorldStageController worldStage)
        {
            _session           = session;
            _runStates         = runStates;
            _world             = world;
            _battleScopePrefab = battleScopePrefab;
            _worldStage        = worldStage;
        }

        /// <summary>Идёт ли бой прямо сейчас — то есть существует ли боевой скоуп.</summary>
        public bool IsOpen => _battle != null;

        /// <summary>
        /// Контейнер идущего боя или <c>null</c>, если боя нет. Нужен dev-инструментам: они живут в
        /// мире и переживают бои, а работают с внутренностями текущего.
        /// </summary>
        /// <remarks>
        /// Это единственная законная дорога к боевым сервисам снаружи боя, и она отдаёт ровно то, что
        /// есть СЕЙЧАС. Прежде консоль искала боевой скоуп статически (<c>LifetimeScope.Find</c>) — пока
        /// скоуп был вечным, это работало; теперь между боями его нет вовсе, и «найти» стало нечего.
        /// </remarks>
        public IObjectResolver BattleContainer => _battle != null ? _battle.Container : null;

        /// <summary>Достать боевой сервис текущего боя. Боя нет — <c>null</c>, и это законный ответ.</summary>
        public T Resolve<T>() where T : class
            => _battle != null && _battle.Container != null ? _battle.Container.Resolve<T>() : null;

        public void Start()
        {
            _session.BindLaunch(Open);
            _session.BindReset(Close);
            _session.BindRestart(Restart);
        }

        public void Dispose()
        {
            _session.UnbindLaunch();
            _session.UnbindReset();
            _session.UnbindRestart();
            Close();
        }

        /// <summary>
        /// Открыть бой: родить скоуп со всем боевым внутри. Идёт ли уже бой — не важно: прошлый
        /// закрывается, потому что двух арен у нас нет.
        /// </summary>
        public void Open(BattlePresetData preset)
        {
            if (preset == null)
            {
                Debug.LogWarning("[BattleHost] - Open без пресета: боя не будет");
                return;
            }
            if (_battleScopePrefab == null)
            {
                Debug.LogError("[BattleHost] - не задан префаб боевого скоупа → бой открыть нечем. " +
                               "Поле _battleScopePrefab у WorldLifetimeScope.");
                return;
            }

            Close();

            _lastPreset = preset;
            var parameters = new BattleScopeParams(preset, SeedFor(preset));

            // Скоуп рождается из ПРЕФАБА, а не из кода: боевые ассеты (тюнинг сима, джус, состав
            // Ристалища) выбираются в инспекторе, и собирать их заново на каждый бой из кода значило бы
            // завести им второго владельца.
            _battle = _world.CreateChildFromPrefab(_battleScopePrefab,
                builder => builder.RegisterInstance(parameters));
            _battle.name = $"[Battle] {preset.Id}";
        }

        /// <summary>
        /// Закрыть бой: скоуп уходит вместе со всем, что в нём жило, и арена возвращается миру.
        /// Боя нет — no-op: закрывать нечего, и это законно (ивент, магазин, привал).
        /// </summary>
        public void Close()
        {
            if (_battle == null) return;

            // Уничтожаем объект, а не только контейнер: Dispose энтрипоинтов случится внутри, и они сами
            // отпустят показ (лента, презентеры, камера). Порядок обратный рождению — так и задумано.
            UnityEngine.Object.Destroy(_battle.gameObject);
            _battle = null;

            // Мир снова показывает свой отряд: боевых тел на арене больше нет, а пустая арена в хабе —
            // это не «после боя», это дефект.
            _worldStage.PlaceParty();
        }

        /// <summary>Перезапустить тот же бой (ретрай узла, dev-R): новый скоуп на тот же пресет.</summary>
        public void Restart()
        {
            if (_lastPreset == null)
            {
                Debug.LogWarning("[BattleHost] - Restart без прошлого боя (нечего перезапускать)");
                return;
            }
            Open(_lastPreset);
        }

        /// <summary>
        /// Сид боя из сохраняемого сида забега плюс акт, узел и пресет: один и тот же узел одного и того
        /// же забега всегда играется одинаково, а соседний — иначе. Номер попытки в сид НЕ входит:
        /// ретрай — это тот же бой, а не новый.
        /// </summary>
        /// <remarks>
        /// Забега может не быть вовсе (dev-бой из консоли, Ристалище) — тогда сид выводится из одного
        /// пресета. Это не фолбэк на отказ: у боя вне забега просто нет забегового слагаемого.
        /// </remarks>
        private ulong SeedFor(BattlePresetData preset)
        {
            RunState run = _runStates?.Current;

            ulong seed = DeterministicHash.Of(preset.Id);
            seed = DeterministicHash.Mix(seed, run != null ? (ulong)run.Seed : 0UL);
            seed = DeterministicHash.Mix(seed, (ulong)(uint)(run?.CurrentActIndex ?? 0));
            seed = DeterministicHash.Mix(seed, DeterministicHash.Of(run?.Map?.CurrentNodeId));
            return seed;
        }
    }
}
