using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Загрузка и выгрузка сцен. Фаза 1 — простая аддитивная загрузка.
    /// NGO Scene Management подключится в Фазе 6 за этим фасадом.
    /// </summary>
    public sealed class SceneLoader
    {
        private const string BattleSceneName = "BattleScene";

        private Scene _loadedBattleScene;

        /// <summary>Аддитивно загрузить BattleScene.</summary>
        public async UniTask LoadBattleAsync()
        {
            if (_loadedBattleScene.isLoaded)
            {
                Debug.LogWarning("[SceneLoader] - BattleScene уже загружена");
                return;
            }

            await SceneManager.LoadSceneAsync(BattleSceneName, LoadSceneMode.Additive);
            _loadedBattleScene = SceneManager.GetSceneByName(BattleSceneName);
            Debug.Log("[SceneLoader] - BattleScene загружена");
        }

        /// <summary>Выгрузить BattleScene после окончания боя.</summary>
        public async UniTask UnloadBattleAsync()
        {
            if (!_loadedBattleScene.isLoaded) return;

            await SceneManager.UnloadSceneAsync(_loadedBattleScene);
            Debug.Log("[SceneLoader] - BattleScene выгружена");
        }
    }
}
