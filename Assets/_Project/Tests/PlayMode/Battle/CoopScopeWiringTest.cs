using System.Collections;
using Guildmaster.Core.Arena;
using Guildmaster.Data.Definitions;
using Guildmaster.Game;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Tests.PlayMode.Battle
{
    /// <summary>
    /// Боевой скоуп обязан подниматься в ОБЕИХ ролях сеанса — и у владельца, и у гостя.
    /// </summary>
    /// <remarks>
    /// <b>Почему этот тест дороже, чем выглядит.</b> 05.08.2026 у гостя не поднимался весь бой
    /// целиком: новый класс просил <c>IStageFrameSource</c>, а тот не был зарегистрирован нигде, и
    /// VContainer ронял <c>Awake</c> скоупа. Игроку это показалось тремя разными поломками в трёх
    /// прогонах — пустое Ристалище, бой без противников, пропавшие круги под ногами, — и ещё одной
    /// сверху: первое подключение вообще не пускало в игру.
    /// <para><b>Ничто из нашего арсенала этого не видело.</b> Компиляция не смотрит в разметку
    /// контейнера. EditMode-тесты соединяют классы РУКАМИ в тесте, а игра соединяет их контейнером
    /// по разметке скоупов — то есть тест собирает систему правильно даже тогда, когда игра собрать
    /// её не может. Единственное, что ловит этот класс поломок, — поднять настоящие скоупы.</para>
    /// <para><b>Роль здесь — не декорация.</b> Состав боевого скоупа ВЫБИРАЕТСЯ ролью сеанса
    /// (<c>CombatLifetimeScope.RegisterCoop</c>), поэтому у гостя и у владельца собираются разные
    /// наборы объектов, и проверять надо оба. Половина сегодняшней поломки жила ровно в той ветке,
    /// которую в одиночку не увидишь.</para>
    /// </remarks>
    public sealed class CoopScopeWiringTest
    {
        private const float BootTimeout = 20f;

        /// <summary>
        /// Чужие ошибки в логе тест не судит — он судит по своим проверкам ниже.
        /// </summary>
        /// <remarks>
        /// Иначе предметом теста становится ЧИСТОТА ЛОГА, а она у нас не чистая по посторонней
        /// причине: UI Toolkit пишет «Advanced Text Generator is disabled but the API is still
        /// called» из своего же обновления панелей (тот же источник, что сотни строк «TextAutoSize is
        /// not supported» в логах живой игры). Гостевая ветка падала на этом, хотя скоуп собирался.
        /// <para>Проверки разводки от этого не слабеют: они ЯВНЫЕ — контейнер собран, шов разрешён и
        /// отвечает. Молчаливо пройти при сломанной регистрации тест не может.</para>
        /// </remarks>
        [SetUp]
        public void IgnoreForeignLogErrors() => LogAssert.ignoreFailingMessages = true;

        // Сброс — на конец КЛАССА, а не каждого теста: сообщение прилетает из обновления панелей уже
        // после тела теста, и флаг, снятый в TearDown, не успевает его прикрыть. Флаг глобальный на
        // прогон, поэтому вернуть его обязательно — иначе следующие классы перестанут судить лог.
        [OneTimeTearDown]
        public void RestoreLogStrictness() => LogAssert.ignoreFailingMessages = false;

        [UnityTest]
        public IEnumerator CombatScope_BuildsForTheOwner()
        {
            yield return CheckRole(SessionRole.Owner);
        }

        [UnityTest]
        public IEnumerator CombatScope_BuildsForTheGuest()
        {
            yield return CheckRole(SessionRole.Guest);
        }

        private static IEnumerator CheckRole(SessionRole role)
        {
            LogAssert.ignoreFailingMessages = true; // см. IgnoreForeignLogErrors — ставим и здесь: SetUp фреймворк перетирает
            yield return LoadGame();

            var root = Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "корневой скоуп не поднялся");

            var sessions   = root.Container.Resolve<SessionHost>();
            var activities = root.Container.Resolve<ActivityHost>();

            sessions.Open(role);
            yield return WaitFrames(1);

            // Ристалище — самое дешёвое место с настоящим боевым скоупом: ни забега, ни узла карты
            // ему не нужно. Именно на нём поломка и вскрылась вживую.
            activities.Open(ActivitySetup.ProvingGrounds);
            yield return WaitFrames(4);

            var combat = LifetimeScope.Find<CombatLifetimeScope>();
            Assert.IsNotNull(combat, $"боевой скоуп не создан в роли «{role}»");
            Assert.IsNotNull(combat.Container,
                $"боевой скоуп в роли «{role}» не собрался — исключение в Awake. " +
                "Смотри в консоли, какой регистрации не хватило.");

            // Резолвим ЯВНО то, что зависит от роли: у владельца за швом живая симуляция, у гостя —
            // присланный кадр, и обе ветки обязаны быть разрешимы. Косвенной проверки «скоуп не упал»
            // мало: энтрипоинт может быть создан лениво и промолчать до первого кадра.
            var arena = combat.Container.Resolve<IArenaUnits>();
            Assert.IsNotNull(arena, $"шов «кто на арене» не разрешился в роли «{role}»");
            Assert.DoesNotThrow(() => { var _ = arena.Units; },
                $"шов «кто на арене» разрешился, но не отвечает в роли «{role}»");

            activities.Close();
            sessions.Close();
            yield return WaitFrames(2);
        }

        /// <summary>Поднять игру так, как она поднимается у игрока: корневая сцена сама грузит мир.</summary>
        private static IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync("CoreScene", LoadSceneMode.Single);

            float deadline = Time.realtimeSinceStartup + BootTimeout;
            while (Object.FindAnyObjectByType<WorldLifetimeScope>() == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"мир не поднялся за {BootTimeout} с — бут сломан");
                yield return null;
            }

            yield return WaitFrames(2);
        }

        /// <summary>
        /// Подождать кадров, а не секунд: VContainer диспатчит энтрипоинты отдельной фазой, и всё, что
        /// случается «на входе в место», происходит не в тот же вызов.
        /// </summary>
        private static IEnumerator WaitFrames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }
    }
}
