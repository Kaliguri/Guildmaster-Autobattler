using Guildmaster.Core.Platform;
using Steamworks;
using UnityEngine;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Открывает ссылки наружу: оверлеем Steam, если он на связи, иначе браузером машины.
    /// </summary>
    /// <remarks>
    /// <b>Оверлей — не украшение, а единственный способ не выкидывать игрока из игры.</b>
    /// <c>Application.OpenURL</c> в полноэкранном режиме сворачивает игру и уводит в браузер; оверлей
    /// открывает страницу поверх кадра, и игрок возвращается одним Shift+Tab.
    /// <para><b>Браузер — законный фолбэк, потому что отказ ВНЕШНИЙ</b> (политика фолбэков, полоса
    /// «внешний отказ»): Steam может быть не запущен вовсе — например, при запуске из редактора или
    /// из собранного плеера мимо клиента. Молчать в этом случае нельзя, а подменять оверлей нечем,
    /// кроме браузера.</para>
    /// <para><b>AppId берётся у <see cref="SteamBootstrap"/></b> — там он объявлен константой и у него
    /// один владелец. Своего номера у этой службы нет намеренно: две копии разошлись бы ровно в тот
    /// день, когда игре выдадут боевой AppId.</para>
    /// </remarks>
    public sealed class ExternalLinkService : IExternalLinkService
    {
        /// <inheritdoc />
        public bool OverlayAvailable => SteamClient.IsValid;

        /// <inheritdoc />
        public void OpenUrl(string url)
        {
            // Пустой адрес — это НЕ ошибка: в ассете сообщества лежат заготовки под ссылки, которых
            // ещё нет, и панель их просто не показывает. Сюда пустая строка доходит только если
            // кто-то позвал службу мимо панели.
            if (string.IsNullOrWhiteSpace(url)) return;

            if (OverlayAvailable)
            {
                SteamFriends.OpenWebOverlay(url);
                return;
            }

            Application.OpenURL(url);
        }

        /// <inheritdoc />
        public void OpenStorePage()
        {
            if (OverlayAvailable)
            {
                // OverlayToStoreFlag.None — просто открыть страницу. Класть игру в корзину мы не
                // просим: кнопка зовёт в желаемое, а не в покупку.
                SteamFriends.OpenStoreOverlay(SteamBootstrap.AppId, Steamworks.OverlayToStoreFlag.None);
                return;
            }

            Application.OpenURL(StorePageUrl);
        }

        /// <summary>Адрес страницы игры в вебе — тот же AppId, только для браузера.</summary>
        public static string StorePageUrl => $"https://store.steampowered.com/app/{SteamBootstrap.AppId}/";
    }
}
