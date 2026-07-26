using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using UnityEngine;
using VContainer;

namespace Guildmaster.Game
{
    /// <summary>
    /// Точка старта: поднимает персистентный мир и запускает верхнюю петлю игры (главное меню → забег →
    /// меню). Dev-флаги ниже подменяют вход разрезом — актом, одиночным боем или текст-ивентом.
    /// Размещается в CoreScene на объекте [Bootstrap].
    /// <para>Вся игра живёт внутри одной задачи, поэтому она обязана быть защищённой: раньше любое
    /// исключение, кроме отмены, убивало петлю навсегда и молча — игра оставалась на экране, не отвечая
    /// ни на что (аудит 2026-07-26, C-03). Теперь падение видно в логе, и петля поднимается заново
    /// с главного меню ограниченное число раз.</para>
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("A2 dev-разрез (план 11)")]
        [Tooltip("ON: на старте прогнать ВЕСЬ АКТ через петлю (карта из сида → узлы авто-обходом). " +
                 "Имеет приоритет над одиночным боем и ивентом. Нужен контент в БД (пресеты/ивенты).")]
        [SerializeField] private bool _runActOnBoot;

        [Tooltip("ON: на старте прогнать один бой через полный BattleFlow (нужен пресет ниже). " +
                 "OFF (по умолчанию): обычный вход — главное меню → забег.")]
        [SerializeField] private bool _runBattleFlowOnBoot;

        [Tooltip("Стартовый бой для A2-разреза (враги + ростер + режим расстановки). Нужен при включённом флаге.")]
        [SerializeField] private BattlePresetData _devStartPreset;

        [Tooltip("ON: на старте показать текстовый ивент (нужен ассект ниже). Имеет приоритет над боем.")]
        [SerializeField] private bool _runTextEventOnBoot;

        [Tooltip("Стартовый текстовый ивент для дебага (StS-style). Нужен при включённом флаге ивента.")]
        [SerializeField] private TextEventData _devStartEvent;

        [Inject] private GameFlow _gameFlow;
        [Inject] private ISceneLoader _sceneLoader;

        /// <summary>Сколько раз поднимать петлю после падения, прежде чем сдаться и сказать об этом вслух.</summary>
        private const int MaxRestarts = 2;

        private void Start()
        {
            // Токен от объекта: при выгрузке сцены/остановке play-mode await'ы прекращаются, а не
            // продолжают жить в оторванной задаче.
            StartBootAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid StartBootAsync(CancellationToken ct)
        {
            try
            {
                await BootAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Норма: выход из play-mode, выгрузка сцены, закрытие игры.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[GameBootstrap] - загрузка мира упала, игра не запущена");
            }
        }

        private async UniTask BootAsync(CancellationToken ct)
        {
            Debug.Log("[GameBootstrap] - Старт");

            // Персистентный мир (камера-риг + арена) поднимаем ПЕРВЫМ и держим всю сессию: вне боя он
            // даёт вид арены (карта/инвентарь), в бою переиспользуется. Бой (BattleScene) ложится поверх.
            await _sceneLoader.LoadWorldAsync();

            // Боевые системы тоже persist (план 12 Ф2): боевой скоуп живёт всю сессию, бой запускается
            // командой в живой sim (RequestLaunch), а не загрузкой сцены на каждый узел. Грузим один раз здесь.
            await _sceneLoader.LoadCombatSystemsAsync();

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
                Debug.LogWarning("[GameBootstrap] - флаг BattleFlow включён, но пресет не назначен → обычный вход");

            await RunGameLoopAsync(ct); // D1: главное меню → забег → меню
        }

        /// <summary>
        /// Верхняя петля под защитой. <see cref="GameFlow.RunGameAsync"/> сам по себе бесконечен и всегда
        /// начинается с главного меню, поэтому после падения его можно поднять заново — игрок теряет
        /// незасчитанный узел, но не сессию. Молчаливую смерть петли не допускаем: она выглядит как
        /// намертво зависшая игра, по которой невозможно понять, что произошло.
        /// </summary>
        private async UniTask RunGameLoopAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt <= MaxRestarts; attempt++)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    await _gameFlow.RunGameAsync();
                    return; // вышли штатно (Выход из меню)
                }
                catch (OperationCanceledException)
                {
                    throw; // отмену пробрасываем: её обрабатывает StartBootAsync
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

                    if (attempt == MaxRestarts)
                    {
                        Debug.LogError($"[GameBootstrap] - петля игры падала {attempt + 1} раз(а) подряд, " +
                                       "перезапуск прекращён");
                        return;
                    }

                    Debug.LogError($"[GameBootstrap] - петля игры упала, поднимаю заново " +
                                   $"(попытка {attempt + 1} из {MaxRestarts})");
                }
            }
        }
    }
}
