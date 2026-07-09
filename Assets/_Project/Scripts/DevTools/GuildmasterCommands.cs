using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using QFSW.QC;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Набор отладочных команд Quantum Console для Фазы 1 (вики «10» §9).
    /// Инъектируется через VContainer — зависимости появляются только при наличии активной симуляции.
    /// </summary>
    public sealed class GuildmasterCommands : MonoBehaviour
    {
        [Tooltip("SO реликвии «Железный копейщик» для gm_spawn_spearman (вики «13» шаг 4).")]
        [SerializeField] private RelicData _spearmanRelic;

        [Tooltip("SO реликвии «Светлый пастырь» для gm_spawn_shepherd (вики «13» §10.1).")]
        [SerializeField] private RelicData _shepherdRelic;

        [Tooltip("SO реликвии «Криомант» для gm_spawn_cryomancer (вики «13» §10.2).")]
        [SerializeField] private RelicData _cryomancerRelic;

        private CombatSimulation   _simulation;
        private CombatDebugDraw    _debugDraw;
        private RuntimeUnitFactory _factory;
        private QuantumConsole     _console;

        [Inject]
        public void Construct(CombatSimulation simulation, CombatDebugDraw debugDraw, RuntimeUnitFactory factory)
        {
            _simulation = simulation;
            _debugDraw  = debugDraw;
            _factory    = factory;
        }

        // Пауза сима, пока консоль открыта: настраиваешь бой за консолью, закрываешь — он идёт с начала
        // на виду (без этого бой проигрывается за полноэкранной консолью и заканчивается невидимым).
        private void Start()
        {
            _console = FindObjectOfType<QuantumConsole>(true);
            if (_console != null)
            {
                _console.OnActivate   += PauseForConsole;
                _console.OnDeactivate += ResumeAfterConsole;
            }
        }

        private void OnDestroy()
        {
            if (_console != null)
            {
                _console.OnActivate   -= PauseForConsole;
                _console.OnDeactivate -= ResumeAfterConsole;
            }
        }

        private void PauseForConsole()   => _simulation?.SetPaused(true);
        private void ResumeAfterConsole() => _simulation?.SetPaused(false);

        /// <summary>Зафиксировать сид боя для детерминизм-отладки (только до старта).</summary>
        [Command("gm_rng_seed", "Зафиксировать сид боя (до старта симуляции)")]
        public void SetRngSeed(ulong seed)
        {
            Debug.Log($"[GuildmasterCommands] - gm_rng_seed {seed}: изменение сида поддерживается только через CombatLifetimeScope до запуска");
        }

        /// <summary>Поднять тест-бой N×M юнитов с заданными HP.</summary>
        [Command("gm_spawn_battle", "Запустить тест-бой N юнитов за каждую сторону")]
        public void SpawnBattle(int countPerTeam = 2, float hp = 300f, float damage = 50f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }

            // Id начинаем от текущего числа живых юнитов в симуляции, чтобы не было коллизий
            // при повторном вызове команды в том же бою.
            int nextId = _simulation.Units.Count;
            for (int i = 0; i < countPerTeam; i++)
            {
                _simulation.EnqueueUnitSpawn(MakeTestUnit(0, new Vector2(-5f + i, i), hp, damage, nextId++));
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2( 5f - i, i), hp, damage, nextId++));
            }

            Debug.Log($"[GuildmasterCommands] - gm_spawn_battle: добавлено {countPerTeam}×2 юнитов");
        }

        /// <summary>Заспавнить «Железного копейщика» (team 0) против кластера болванчиков (team 1) — срез шага 4.</summary>
        [Command("gm_spawn_spearman", "Заспавнить Железного копейщика против кластера (срез шага 4)")]
        public void SpawnSpearman(int enemies = 3, float enemyHp = 200f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_spearmanRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _spearmanRelic в инспекторе"); return; }

            // Копейщик слева — через фабрику (реальный путь сборки: статы/линейная АА/активка/AI-профиль/мана).
            _simulation.EnqueueUnitSpawn(_factory.Create(_spearmanRelic, null, team: 0, new Vector2(-5f, 0f)));

            // Кластер болванчиков справа — чтобы линейная АА задевала нескольких и сработало условие «≥2 в радиусе».
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 0.8f; // компактно по вертикали
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(5f, y), enemyHp, 8f, nextId++));
            }

            Debug.Log($"[GuildmasterCommands] - gm_spawn_spearman: копейщик vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Светлого пастыря» (team 0) + раненых союзников против болванчиков — срез §10.1.</summary>
        [Command("gm_spawn_shepherd", "Заспавнить Светлого пастыря + раненых союзников против болванчиков (срез §10.1)")]
        public void SpawnShepherd(int allies = 2, float enemyHp = 200f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_shepherdRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _shepherdRelic в инспекторе"); return; }

            // Пастырь в тылу слева — через фабрику (реальный путь: AI-профиль Heal, хил-снаряд, активка «Длань жизни»).
            _simulation.EnqueueUnitSpawn(_factory.Create(_shepherdRelic, null, team: 0, new Vector2(-6f, 0f)));

            int nextId = _simulation.Units.Count + 1;

            // Раненые союзники (team 0) на фронте: старт на 40% HP — видно выбор раненого и хил-снаряды.
            for (int i = 0; i < allies; i++)
            {
                float y = (i - (allies - 1) * 0.5f) * 1.2f;
                var ally = MakeTestUnit(0, new Vector2(-3f, y), 200f, 12f, nextId++);
                ally.CurrentHP = 80f; // 40% от 200 — есть кого лечить
                _simulation.EnqueueUnitSpawn(ally);
            }

            // Пара болванчиков справа (team 1) — чтобы союзники завязли в бою и просаживались под «Длань».
            for (int i = 0; i < 2; i++)
            {
                float y = (i - 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), enemyHp, 10f, nextId++));
            }

            Debug.Log($"[GuildmasterCommands] - gm_spawn_shepherd: пастырь + {allies} раненых союзника vs 2 болванчика");
        }

        /// <summary>Заспавнить «Криоманта» (team 0) против кластера болванчиков (team 1) — срез §10.2.</summary>
        [Command("gm_spawn_cryomancer", "Заспавнить Криоманта против кластера болванчиков (срез §10.2)")]
        public void SpawnCryomancer(int enemies = 3, float enemyHp = 200f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_cryomancerRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _cryomancerRelic в инспекторе"); return; }

            // Криомант в тылу слева — через фабрику (реальный путь: on-hit «Заморозка», масс-стан «Ледяные оковы», AI PreferUntagged).
            _simulation.EnqueueUnitSpawn(_factory.Create(_cryomancerRelic, null, team: 0, new Vector2(-6f, 0f)));

            // Кластер болванчиков справа: пока Криомант раздаёт «Заморозку», их накапливается ≥2 → срабатывают «Ледяные оковы».
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1f;
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), enemyHp, 8f, nextId++));
            }

            Debug.Log($"[GuildmasterCommands] - gm_spawn_cryomancer: криомант vs {enemies} болванчиков");
        }

        /// <summary>Выставить HP юниту по ID.</summary>
        [Command("gm_set_hp", "Выставить HP юниту по ID")]
        public void SetHp(int unitId, float hp)
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Id == unitId)
                {
                    unit.CurrentHP = Mathf.Max(0f, hp);
                    Debug.Log($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} HP = {unit.CurrentHP}");
                    return;
                }
            }

            Debug.LogWarning($"[GuildmasterCommands] - gm_set_hp: юнит {unitId} не найден");
        }

        /// <summary>Мгновенно завершить бой (убить всех из команды 1).</summary>
        [Command("gm_skip_battle", "Мгновенно завершить бой в пользу команды A")]
        public void SkipBattle()
        {
            if (_simulation == null) return;

            for (int i = 0; i < _simulation.Units.Count; i++)
            {
                var unit = _simulation.Units[i];
                if (unit.Team == 1) unit.CurrentHP = -1f;
            }

            Debug.Log("[GuildmasterCommands] - gm_skip_battle: все юниты команды B убиты");
        }

        /// <summary>Перезагрузить боевую сцену для нового прогона (бой одноразовый: после конца loop останавливается).</summary>
        [Command("gm_restart", "Перезапустить бой (перезагрузка сцены)")]
        public void Restart()
        {
            Scene active = SceneManager.GetActiveScene();
            Debug.Log($"[GuildmasterCommands] - gm_restart: перезагружаю {active.name}");
            SceneManager.LoadScene(active.name);
        }

        /// <summary>Включить/выключить Shapes debug-слой.</summary>
        [Command("gm_toggle_debug_draw", "Вкл/выкл debug-отрисовку боя")]
        public void ToggleDebugDraw()
        {
            if (_debugDraw == null) { Debug.LogWarning("[GuildmasterCommands] - CombatDebugDraw не найден"); return; }
            _debugDraw.IsEnabled = !_debugDraw.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_debug_draw: {(_debugDraw.IsEnabled ? "ON" : "OFF")}");
        }

        private static RuntimeUnit MakeTestUnit(int team, Vector2 pos, float hp, float damage, int id)
        {
            var stats = new Stats(null);
            stats.AddModifiersFrom("test", new[]
            {
                new StatModifier(StatType.MaxHP,            ModifierOp.Flat, hp),
                new StatModifier(StatType.AutoAttackDamage, ModifierOp.Flat, damage),
                new StatModifier(StatType.AttackSpeed,      ModifierOp.Flat, 1f),
                new StatModifier(StatType.AttackRange,      ModifierOp.Flat, 1.5f),
                new StatModifier(StatType.MoveSpeed,        ModifierOp.Flat, 3f),
            });
            return new RuntimeUnit
            {
                Id               = id,
                Team             = team,
                Stats            = stats,
                CurrentHP        = hp,
                Position         = pos,
                PreviousPosition = pos,
            };
        }
    }
}
