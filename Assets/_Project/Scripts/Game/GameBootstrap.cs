using Cysharp.Threading.Tasks;
using Guildmaster.Game.Services;
using UnityEngine;
using VContainer;

namespace Guildmaster.Game
{
    /// <summary>
    /// Точка старта: поднимает <see cref="RootLifetimeScope"/> и запускает <see cref="GameFlow.BootAsync"/>.
    /// Размещается в CoreScene на объекте [Bootstrap].
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Inject] private GameFlow _gameFlow;

        private void Start()
        {
            StartBootAsync().Forget();
        }

        private async UniTaskVoid StartBootAsync()
        {
            Debug.Log("[GameBootstrap] - Старт");
            await _gameFlow.BootAsync();
        }
    }
}
