using System;
using Steamworks;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Поднимает Steam на старте игры и качает его колбэки. Без этого не работает НИЧЕГО сетевое:
    /// ни лобби, ни приглашения, ни relay-сокет.
    /// </summary>
    /// <remarks>
    /// <b>Зачем отдельный класс и почему он появился поздно.</b> Инициализация жила в
    /// <c>FacepunchTransportBootstrap</c> и уехала 02.08.2026 вместе с высокоуровневым netcode — файл
    /// делал две работы, а удалялся за одну. Симптом был бы тихий и обидный: <c>SteamClient.IsValid</c>
    /// возвращает <c>false</c>, лобби «просто не создаётся», кнопка приглашения гаснет — то есть игра
    /// ведёт себя ровно так, как при незапущенном Steam, и искать это пришлось бы на живом тесте вдвоём.
    /// <para><b>Колбэки качаем сами</b> (<c>asyncCallbacks: false</c>): фоновый поток Facepunch отдавал
    /// бы события лобби и приглашений не в главном потоке Unity, а трогают они наш UI и сеанс. Цена —
    /// один вызов в кадр.</para>
    /// <para><b>Steam не запущен — это внешний отказ, и он честный:</b> говорим вслух и живём дальше
    /// одиночной игрой. Подменять его нечем, а молчание читалось бы как «кооп сломан».</para>
    /// </remarks>
    public sealed class SteamBootstrap : Guildmaster.Core.Players.IPlatformIdentity, ITickable, IDisposable
    {
        /// <summary>
        /// Слот, под которым мы ходим в Steam. Пока это неизданная <i>Few Seconds - Many Deaths!</i>
        /// (решение Макса 01.08.2026): свой покупается ближе к демке, переезд стоит одного числа.
        /// </summary>
        /// <remarks>
        /// Проверять кооп на Spacewar (480) нельзя, хотя обычно тестируют на нём: им владеют все, и
        /// потому он не проверяет главное — что лобби и relay работают под приложением с ОГРАНИЧЕННЫМ
        /// кругом владельцев. Следствие для теста: у второго игрока должен быть доступ к этому AppId.
        /// </remarks>
        public const uint AppId = 3259720;

        /// <summary>Как зовут игрока за этим клиентом. Steam не поднят — безымянное «Игрок».</summary>
        /// <remarks>
        /// Живёт здесь, потому что здесь единственное место, где мы вообще говорим со Steam про личность.
        /// Спрашивать <c>SteamClient.Name</c> из игрового кода значило бы завести второй вход в Steam —
        /// и второе поведение на случай, когда Steam не запущен.
        /// </remarks>
        public string PlayerName => _running ? SteamClient.Name : "Игрок";

        private bool _running;

        /// <summary>Поднялся ли Steam. Спрашивают лобби и транспорт — им без него делать нечего.</summary>
        public bool IsReady => _running && SteamClient.IsValid;

        /// <summary>
        /// Поднимаем Steam В КОНСТРУКТОРЕ, а не на <c>Start</c>.
        /// </summary>
        /// <remarks>
        /// Потому что от ответа «поднялся или нет» зависит СОСТАВ контейнера: транспорт выбирается по
        /// нему (см. <c>RootLifetimeScope</c>). Фаза <c>Start</c> идёт уже после того, как все объекты
        /// созданы, — спрашивать там значило бы выбирать транспорт до того, как ответ известен.
        /// </remarks>
        public SteamBootstrap()
        {
            try
            {
                SteamClient.Init(AppId, asyncCallbacks: false);
                _running = true;
                Debug.Log($"[SteamBootstrap] Steam на связи: {SteamClient.Name} (AppId {AppId})");
            }
            catch (Exception e)
            {
                // Ровно два обычных случая: Steam не запущен или у аккаунта нет доступа к этому AppId.
                // Оба — снаружи, оба лечатся не кодом, поэтому текст должен называть их прямо.
                Debug.LogWarning($"[SteamBootstrap] Steam не поднялся ({e.Message}). Кооп недоступен: " +
                                 "проверь, запущен ли клиент и есть ли у аккаунта доступ к AppId " +
                                 $"{AppId}. Одиночная игра работает как обычно.");
            }
        }

        public void Tick()
        {
            if (!_running) return;
            SteamClient.RunCallbacks();
        }

        public void Dispose()
        {
            if (!_running) return;

            _running = false;
            SteamClient.Shutdown();
        }
    }
}
