using Guildmaster.Combat;
using Guildmaster.Combat.Commands;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using QFSW.QC;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [Tooltip("SO реликвии «Надёжный защитник» для gm_spawn_defender (вики «13» §10.3).")]
        [SerializeField] private RelicData _defenderRelic;

        [Tooltip("SO реликвии «Лесной следопыт» для gm_spawn_ranger (вики «13» §10.4).")]
        [SerializeField] private RelicData _rangerRelic;

        [Tooltip("SO реликвии «Скрытный убийца» для gm_spawn_assassin (вики «13» §10.5).")]
        [SerializeField] private RelicData _assassinRelic;

        [Tooltip("SO реликвии «Монах вихря» для gm_spawn_monk (вики «13» §10.6).")]
        [SerializeField] private RelicData _monkRelic;

        private CombatSimulation   _simulation;
        private CombatDebugDraw    _debugDraw;
        private RuntimeUnitFactory _factory;
        private QuantumConsole     _console;

        // Последний сетап боя для быстрого перезапуска по R. static — переживает релоад сцены.
        private static System.Action<GuildmasterCommands> _lastBattleSetup;
        private static bool _replayLastBattleOnStart;

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

        // Dev-хоткеи (new Input System): F5 — релоад сцены (пустая арена), R — перезапуск последнего боя.
        private void Update()
        {
            // Отложенный повтор последнего сетапа после R-релоада — как только симуляция готова.
            if (_replayLastBattleOnStart && _simulation != null)
            {
                _replayLastBattleOnStart = false;
                _lastBattleSetup?.Invoke(this);
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) Restart();
            if (kb.rKey.wasPressedThisFrame)  RestartLastBattle();
        }

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

            _lastBattleSetup = self => self.SpawnBattle(countPerTeam, hp, damage);
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

            _lastBattleSetup = self => self.SpawnSpearman(enemies, enemyHp);
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

            _lastBattleSetup = self => self.SpawnShepherd(allies, enemyHp);
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

            _lastBattleSetup = self => self.SpawnCryomancer(enemies, enemyHp);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_cryomancer: криомант vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Надёжного защитника» (team 0) против ударных болванчиков (team 1) — срез §10.3.</summary>
        [Command("gm_spawn_defender", "Заспавнить Надёжного защитника против ударных болванчиков (срез §10.3)")]
        public void SpawnDefender(int enemies = 3, float enemyDamage = 40f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_defenderRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _defenderRelic в инспекторе"); return; }

            // Защитник по центру-слева — через фабрику (реальный путь: пассив «Оплот» pre-damage, HighestThreat, ульта).
            _simulation.EnqueueUnitSpawn(_factory.Create(_defenderRelic, null, team: 0, new Vector2(-4f, 0f)));

            // Ударные болванчики справа: урон выше порога «Оплота» (15% × 220 ≈ 33), чтобы щит поднимался.
            // Первый бьёт сильнее — видно, что ульта уходит в «главную угрозу» (HighestThreat).
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                float dmg = i == 0 ? enemyDamage * 1.5f : enemyDamage; // главный ДПС — первый
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), 160f, dmg, nextId++));
            }

            _lastBattleSetup = self => self.SpawnDefender(enemies, enemyDamage);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_defender: защитник vs {enemies} ударных болванчиков");
        }

        /// <summary>Заспавнить «Лесного следопыта» (team 0) против кластера болванчиков (team 1) — срез §10.4.</summary>
        [Command("gm_spawn_ranger", "Заспавнить Лесного следопыта против кластера болванчиков (срез §10.4)")]
        public void SpawnRanger(int enemies = 3, float enemyHp = 120f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_rangerRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _rangerRelic в инспекторе"); return; }

            // Следопыт слева — через фабрику (реальный путь: кайт, стрельба на ходу, «Метка охотника» с переносом).
            _simulation.EnqueueUnitSpawn(_factory.Create(_rangerRelic, null, team: 0, new Vector2(-6f, 0f)));

            // Кластер болванчиков справа лезет в ближний бой — видно кайт (отход) и стрельбу на ходу.
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), enemyHp, 10f, nextId++));
            }

            _lastBattleSetup = self => self.SpawnRanger(enemies, enemyHp);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_ranger: следопыт vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Скрытного убийцу» (team 0) против болванчиков (team 1) — срез §10.5.</summary>
        [Command("gm_spawn_assassin", "Заспавнить Скрытного убийцу против болванчиков (срез §10.5)")]
        public void SpawnAssassin(int enemies = 3, float enemyHp = 120f, float enemyDamage = 40f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_assassinRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _assassinRelic в инспекторе"); return; }

            // Убийца слева — через фабрику (реальный путь: пассивы «Скрытность» + «Изворотливость» из GrantedEffects,
            // усиленный первый удар, негейт крупных ударов, рестелс после убийства).
            _simulation.EnqueueUnitSpawn(_factory.Create(_assassinRelic, null, team: 0, new Vector2(-5f, 0f)));

            // Болванчики справа: бьют крупно (> порога «Изворотливости»), чтобы видеть негейт и заряды.
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 1.2f;
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), enemyHp, enemyDamage, nextId++));
            }

            _lastBattleSetup = self => self.SpawnAssassin(enemies, enemyHp, enemyDamage);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_assassin: убийца vs {enemies} болванчиков");
        }

        /// <summary>Заспавнить «Монаха вихря» (team 0) против кластера болванчиков (team 1) — срез §10.6.</summary>
        [Command("gm_spawn_monk", "Заспавнить Монаха вихря против кластера болванчиков (срез §10.6)")]
        public void SpawnMonk(int enemies = 3, float enemyHp = 150f)
        {
            if (_simulation == null) { Debug.LogWarning("[GuildmasterCommands] - Симуляция не активна"); return; }
            if (_factory == null)    { Debug.LogWarning("[GuildmasterCommands] - RuntimeUnitFactory не внедрён"); return; }
            if (_monkRelic == null) { Debug.LogWarning("[GuildmasterCommands] - Не задан _monkRelic в инспекторе"); return; }

            // Монах слева — через фабрику (реальный путь: «Шквальный толчок» = отбрасывание + «ядро» по линии,
            // «Вихревой заход» = телепорт к цели + усиленный удар в конце полёта).
            _simulation.EnqueueUnitSpawn(_factory.Create(_monkRelic, null, team: 0, new Vector2(-5f, 0f)));

            // Кластер болванчиков справа: чтобы «ядро» пролетало сквозь нескольких (толкаем одного в других).
            int nextId = _simulation.Units.Count + 1;
            for (int i = 0; i < enemies; i++)
            {
                float y = (i - (enemies - 1) * 0.5f) * 0.7f; // компактно — толкаемый задевает соседей
                _simulation.EnqueueUnitSpawn(MakeTestUnit(1, new Vector2(4f, y), enemyHp, 10f, nextId++));
            }

            _lastBattleSetup = self => self.SpawnMonk(enemies, enemyHp);
            Debug.Log($"[GuildmasterCommands] - gm_spawn_monk: монах vs {enemies} болванчиков");
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

        /// <summary>R: перезапустить ПОСЛЕДНИЙ бой — релоад сцены + повтор последнего сетапа (dev-итерация).</summary>
        [Command("gm_restart_battle", "Перезапустить последний бой (релоад сцены + повтор последнего сетапа)")]
        public void RestartLastBattle()
        {
            if (_lastBattleSetup == null)
            {
                Debug.LogWarning("[GuildmasterCommands] - gm_restart_battle: последний бой не задан (сначала запусти любой gm_spawn_*)");
                return;
            }
            _replayLastBattleOnStart = true;
            Restart();
        }

        /// <summary>Включить/выключить Shapes debug-слой.</summary>
        [Command("gm_toggle_debug_draw", "Вкл/выкл debug-отрисовку боя")]
        public void ToggleDebugDraw()
        {
            if (_debugDraw == null) { Debug.LogWarning("[GuildmasterCommands] - CombatDebugDraw не найден"); return; }
            _debugDraw.IsEnabled = !_debugDraw.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_debug_draw: {(_debugDraw.IsEnabled ? "ON" : "OFF")}");
        }

        /// <summary>Включить/выключить dev-слой статус-колец (метка/стан/щит/заморозка/усиление).</summary>
        [Command("gm_toggle_status", "Вкл/выкл dev-подсветку статусов юнитов (кольца)")]
        public void ToggleStatusOverlay()
        {
            var overlay = FindObjectOfType<CombatStatusOverlay>(true);
            if (overlay == null) { Debug.LogWarning("[GuildmasterCommands] - CombatStatusOverlay не найден (создаётся в бою)"); return; }
            overlay.IsEnabled = !overlay.IsEnabled;
            Debug.Log($"[GuildmasterCommands] - gm_toggle_status: {(overlay.IsEnabled ? "ON" : "OFF")}");
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
