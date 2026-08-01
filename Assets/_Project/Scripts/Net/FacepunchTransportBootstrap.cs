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
        /// <remarks>
        /// AppId нашего же неизданного проекта, а не Spacewar (480): у 480 владеют все, поэтому на нём
        /// не проверить главное — что лобби и relay работают под приложением, которым владеет
        /// ограниченный круг. Цена ровно в этом: **тестировать может только аккаунт с доступом**, так
        /// что второй тестовый аккаунт нужно завести в Steamworks заранее. Свой слот покупается ближе к
        /// демке (решение Макса 01.08.2026) — переезд стоит одного числа здесь.
        /// </remarks>
        [Tooltip("Steam AppId. 3259720 = Few Seconds - Many Deaths!, наш тестовый слот до покупки своего.")]
        [SerializeField] private uint _appId = 3259720;

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
