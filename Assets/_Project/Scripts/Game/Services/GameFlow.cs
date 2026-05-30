using Cysharp.Threading.Tasks;
using Guildmaster.Combat;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Управляет макро-флоу игры: Boot → загрузка BattleScene → старт боя → результат.
    /// Фаза 1 — тонкий слой: сразу грузит BattleScene.
    /// Полный GameFlow (карта, магазины, переходы) — Фаза 5.
    /// </summary>
    public sealed class GameFlow
    {
        private readonly SceneLoader _sceneLoader;

        public GameFlow(SceneLoader sceneLoader)
        {
            _sceneLoader = sceneLoader;
        }

        /// <summary>Запустить стартовый флоу: загрузить боевую сцену.</summary>
        public async UniTask BootAsync()
        {
            Debug.Log("[GameFlow] - Boot: загружаю BattleScene");
            await _sceneLoader.LoadBattleAsync();
        }

        /// <summary>Вызвать после завершения боя: выгрузить боевую сцену.</summary>
        public async UniTask OnBattleEndedAsync(BattleOutcome outcome)
        {
            Debug.Log($"[GameFlow] - Бой завершён: {outcome}");
            await _sceneLoader.UnloadBattleAsync();
        }
    }
}
