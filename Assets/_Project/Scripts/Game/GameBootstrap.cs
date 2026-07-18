using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using UnityEngine;
using VContainer;

namespace Guildmaster.Game
{
    /// <summary>
    /// Точка старта: поднимает <see cref="RootLifetimeScope"/> и запускает флоу. По умолчанию — legacy-вход
    /// (просто грузит BattleScene, бои запускает dev-панель F2). Флаг <see cref="_runBattleFlowOnBoot"/> +
    /// назначенный пресет включают A2-разрез: прогон одного боя через <c>BattleFlow</c> (Prep→Combat→Outcome).
    /// Размещается в CoreScene на объекте [Bootstrap].
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("A2 dev-разрез (план 11)")]
        [Tooltip("ON: на старте прогнать ВЕСЬ АКТ через петлю (карта из сида → узлы авто-обходом). " +
                 "Имеет приоритет над одиночным боем и ивентом. Нужен контент в БД (пресеты/ивенты).")]
        [SerializeField] private bool _runActOnBoot;

        [Tooltip("ON: на старте прогнать один бой через полный BattleFlow (нужен пресет ниже). " +
                 "OFF (по умолчанию): legacy — грузить BattleScene, бой запускать dev-панелью F2.")]
        [SerializeField] private bool _runBattleFlowOnBoot;

        [Tooltip("Стартовый бой для A2-разреза (враги + ростер + режим расстановки). Нужен при включённом флаге.")]
        [SerializeField] private BattlePresetData _devStartPreset;

        [Tooltip("ON: на старте показать текстовый ивент (нужен ассект ниже). Имеет приоритет над боем.")]
        [SerializeField] private bool _runTextEventOnBoot;

        [Tooltip("Стартовый текстовый ивент для дебага (StS-style). Нужен при включённом флаге ивента.")]
        [SerializeField] private TextEventData _devStartEvent;

        [Tooltip("ON: legacy-вход — грузить BattleScene, бой запускать F2-панелью (без главного меню). " +
                 "OFF (по умолчанию): главное меню → забег (D1).")]
        [SerializeField] private bool _legacyBattleScene;

        [Inject] private GameFlow _gameFlow;
        [Inject] private ISceneLoader _sceneLoader;

        private void Start()
        {
            StartBootAsync().Forget();
        }

        private async UniTaskVoid StartBootAsync()
        {
            Debug.Log("[GameBootstrap] - Старт");

            // Персистентный мир (камера-риг + арена) поднимаем ПЕРВЫМ и держим всю сессию: вне боя он
            // даёт вид арены (карта/инвентарь), в бою переиспользуется. Бой (BattleScene) ложится поверх.
            await _sceneLoader.LoadWorldAsync();

            if (_runActOnBoot)
            {
                Flow.EventResult act = await _gameFlow.RunActAsync();
                Debug.Log($"[GameBootstrap] - забег (акт) завершён: {act.Outcome}");
                return;
            }

            if (_runTextEventOnBoot && _devStartEvent != null)
            {
                Flow.EventResult ev = await _gameFlow.RunTextEventAsync(_devStartEvent);
                Debug.Log($"[GameBootstrap] - dev текст-ивент завершён: {ev.Outcome}");
                return;
            }

            if (_runBattleFlowOnBoot && _devStartPreset != null)
            {
                Flow.EventResult result = await _gameFlow.RunSingleBattleAsync(_devStartPreset);
                Debug.Log($"[GameBootstrap] - A2-разрез завершён: {result.Outcome}");
                return;
            }

            if (_runBattleFlowOnBoot)
                Debug.LogWarning("[GameBootstrap] - флаг BattleFlow включён, но пресет не назначен → legacy-вход");

            if (_legacyBattleScene)
            {
                await _gameFlow.BootAsync(); // legacy: грузить BattleScene, бой запускать F2-панелью
                return;
            }

            await _gameFlow.RunGameAsync(); // D1: главное меню → забег → меню
        }
    }
}
