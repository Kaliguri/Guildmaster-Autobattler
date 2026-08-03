using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Загрузка сцен сессии. Обе — persist: грузятся один раз на буте и живут до конца сессии.
    /// NGO Scene Management подключится в Фазе 6 за фасадом <see cref="ISceneLoader"/>.
    /// </summary>
    public sealed class SceneLoader : ISceneLoader
    {
        private const string CombatSystemsSceneName = "CombatSystemsScene";
        private const string WorldSceneName = "WorldScene";

        /// <summary>
        /// Аддитивно загрузить персистентную WorldScene (единый мир: камера-риг + арена).
        /// Грузится один раз на буте и НЕ выгружается — переживает бои.
        /// </summary>
        public async UniTask LoadWorldAsync()
        {
            if (SceneManager.GetSceneByName(WorldSceneName).isLoaded)
            {
                Debug.LogWarning("[SceneLoader] - WorldScene уже загружена");
                return;
            }

            await SceneManager.LoadSceneAsync(WorldSceneName, LoadSceneMode.Additive);
            Debug.Log("[SceneLoader] - WorldScene загружена (persist)");
        }

        /// <summary>
        /// Аддитивно загрузить персистентную сцену боевых систем. Тоже один раз на буте: бой запускается
        /// командой в живой симуляции, так что выгружать её между узлами нечего и незачем.
        /// </summary>
        public async UniTask LoadCombatSystemsAsync()
        {
            if (SceneManager.GetSceneByName(CombatSystemsSceneName).isLoaded)
            {
                Debug.LogWarning("[SceneLoader] - CombatSystemsScene уже загружена");
                return;
            }

            await SceneManager.LoadSceneAsync(CombatSystemsSceneName, LoadSceneMode.Additive);
            Debug.Log("[SceneLoader] - CombatSystemsScene загружена (persist)");
        }
    }
}
