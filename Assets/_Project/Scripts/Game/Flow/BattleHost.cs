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
    /// на выходе. Живёт в скоупе мероприятия — потому что тот, кто создаёт бой, не может жить внутри
    /// него, а заказывает бой именно мероприятие.
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
    public sealed class BattleHost : IDisposable
    {
        private readonly IBattleSession   _session;
        private readonly IRunStateView    _runStates;
        // Скоуп мероприятия, внутри которого мы живём: от него и рождается бой. VContainer кладёт в
        // контейнер сам скоуп, поэтому спрашивать его отдельно не нужно.
        private readonly LifetimeScope    _activity;
        private readonly Activity.BattleScopePrefab _battleScopePrefab;
        private readonly WorldStageController _worldStage;

        private CombatLifetimeScope _battle;
        private BattlePresetData    _lastPreset;

        public BattleHost(IBattleSession session, IRunStateView runStates, LifetimeScope activity,
                          Activity.BattleScopePrefab battleScopePrefab, WorldStageController worldStage)
        {
            _session           = session;
            _runStates         = runStates;
            _activity          = activity;
            _battleScopePrefab = battleScopePrefab;
            _worldStage        = worldStage;

            // Шов привязывается ЗДЕСЬ, а не в Start(): точки входа VContainer диспатчит на следующем
            // кадре, а узел заказывает бой в том же кадре, в котором родилось мероприятие. Пока привязка
            // жила в Start, первый бой такого забега уходил в Aborted («некому запустить бой») и игрок
            // возвращался в главное меню — замер 22.08.2026 показал launchBound=false сразу после
            // ActivityHost.Open и true кадром позже.
            _session.BindLaunch(Open);
            _session.BindReset(Close);
            _session.BindRestart(Restart);
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
            if (_battleScopePrefab?.Value == null)
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
            _battle = _activity.CreateChildFromPrefab(_battleScopePrefab.Value,
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

            // Dispose самого скоупа, а НЕ Destroy его объекта: он диспозит контейнер синхронно (и уже
            // потом уничтожает объект), поэтому энтрипоинты отпускают показ, ленту и камеру ЗДЕСЬ, а не
            // в конце кадра. Через Destroy умирающий бой успевал отвязаться уже после того, как рестарт
            // привязал новый — то есть гасил арену только что начавшегося боя.
            _battle.Dispose();
            _battle = null;

            // Мир снова показывает свой отряд: боевых тел на арене больше нет, а пустая арена в хабе —
            // это не «после боя», это дефект.
            _worldStage.PlaceParty();
        }

        /// <summary>
        /// Открыть арену БЕЗ пресета: скоуп боя есть, состава нет. Так живёт Ристалище — бойцов ему
        /// приносит заказ состава, а не пресет энкаунтера.
        /// </summary>
        /// <remarks>
        /// Дверь отдельная и явная, потому что пустой пресет в <see cref="Open"/> — это ошибка вызова
        /// («бой заказали, а чем — не сказали»), а здесь пустота и есть смысл: площадка открывается
        /// прежде, чем на ней кого-то расставили. Без этой двери владельца расстановки в момент входа
        /// на площадку не существует, и площадка не отвечает ни на один интент.
        /// </remarks>
        public void OpenEmpty()
        {
            if (_battleScopePrefab?.Value == null)
            {
                Debug.LogError("[BattleHost] - не задан префаб боевого скоупа → арену открыть нечем. " +
                               "Поле _battleScopePrefab у WorldLifetimeScope.");
                return;
            }

            Close();

            _lastPreset = null;
            var parameters = new BattleScopeParams(null, DeterministicHash.Of("proving_grounds"));

            _battle = _activity.CreateChildFromPrefab(_battleScopePrefab.Value,
                builder => builder.RegisterInstance(parameters));
            _battle.name = "[Battle] пустая арена";
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
