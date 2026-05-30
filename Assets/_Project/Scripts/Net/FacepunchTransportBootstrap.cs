using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Guildmaster.Net
{
    /// <summary>
    /// Инициализирует Steam через Facepunch.Steamworks и Netcode for GameObjects.
    /// Устанавливает Facepunch Transport как транспорт NGO — Steam relay / NAT бесплатно.
    /// (вики «10» §6.1).
    /// </summary>
    public sealed class FacepunchTransportBootstrap : MonoBehaviour
    {
        [Tooltip("Steam AppId для разработки (480 = Spacewar, тестовое приложение).")]
        [SerializeField] private uint _appId = 480;

        private void Awake()
        {
            if (!SteamClient.IsValid)
            {
                try
                {
                    SteamClient.Init(_appId, false);
                    Debug.Log($"[FacepunchTransportBootstrap] - Steam инициализирован, AppId={_appId}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[FacepunchTransportBootstrap] - Steam init ошибка: {e.Message}");
                }
            }
        }

        private void Update()
        {
            if (SteamClient.IsValid) SteamClient.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (SteamClient.IsValid) SteamClient.Shutdown();
        }
    }
}
