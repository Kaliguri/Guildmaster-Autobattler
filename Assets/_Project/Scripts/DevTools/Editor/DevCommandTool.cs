#if MCP_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Core.DevConsole;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.DevTools.Editor
{
    /// <summary>
    /// Дев-консоль как инструмент агента: строка уходит в тот же <see cref="DevCommandRegistry"/>,
    /// что и набранная руками, обратно приходит статус и текст ответа.
    /// </summary>
    /// <remarks>
    /// <para>Зачем именно так. Чужие агентские харнессы работают в play-mode симуляцией ввода — клик
    /// по кнопке, нажатие клавиши, запись и повтор. Нам это не нужно: командный канал внутрь игры у
    /// нас уже есть и он надёжнее клика — одна строка, один разобранный результат. Симуляция ввода
    /// вдобавок зависит от раскладки и от того, что именно сейчас на экране; команда — нет.</para>
    /// <para><b>Доступен весь реестр, без белого списка</b> (решение Макса 2026-08-02). Команды и так
    /// dev-only, а половинчатый доступ погнал бы агента в обход — через <c>execute_code</c>, то есть
    /// ровно в тот обход шва, против которого инструмент и заведён.</para>
    /// <para>Реестр берётся из живого контейнера, а не из статического поля: синглтоны у нас
    /// запрещены, а <see cref="LifetimeScope"/> в сцене уже держит нужный экземпляр. Отсюда же
    /// главное ограничение — <b>вне play-mode команд нет</b>: без запущенной игры контейнер не
    /// собран, и честный отказ здесь лучше, чем пустой список команд, который читается как «команды
    /// кончились».</para>
    /// </remarks>
    [McpForUnityTool("dev_command",
        Description = "Выполнить команду дев-консоли Guildmaster в работающей игре (play mode). " +
                      "Без параметра command возвращает список доступных команд.")]
    public static class DevCommandTool
    {
        /// <summary>
        /// Схема параметров для агента. Имена свойств уходят в схему <b>как есть</b>
        /// (<c>ToolDiscoveryService</c> берёт <c>prop.Name</c> без приведения к snake_case).
        /// </summary>
        public class Parameters
        {
            [ToolParameter("Строка команды с аргументами, как в консоли: «spawn_battle 3». " +
                           "Пусто — вернуть список команд вместо выполнения.", Required = false)]
            public string command { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (!EditorApplication.isPlaying)
                return new ErrorResponse("not_playing: дев-консоль живёт в работающей игре — " +
                                         "войди в play mode (manage_editor), потом повторяй команду.");

            if (!TryFindRegistry(out DevCommandRegistry registry, out string failure))
                return new ErrorResponse(failure);

            string line = @params?["command"]?.ToString();
            if (string.IsNullOrWhiteSpace(line))
                return new SuccessResponse($"Команд в реестре: {registry.Count}.", new
                {
                    // Сигнатура идёт вместе с именем: без неё агент узнает про аргументы только
                    // получив BadArgs, то есть ценой лишнего круга.
                    commands = registry.All.Select(c => new
                    {
                        name = c.Name,
                        summary = c.Summary,
                        usage = c.Params.Count == 0
                            ? c.Name
                            : $"{c.Name} {string.Join(" ", c.Params.Select(p => p.ToString()))}"
                    }).ToArray()
                });

            DevCommandResult result;
            try
            {
                result = registry.Execute(line);
            }
            catch (Exception e)
            {
                // Реестр ловит исключения тела команды сам и отдаёт их статусом Threw. Сюда падает
                // только то, что сломалось в самом разборе, — это уже дефект, а не плохой ввод.
                return new ErrorResponse($"registry_failed: {e.Message}");
            }

            var data = new { status = result.Status.ToString(), message = result.Message, command = line };
            return result.IsError
                ? new ErrorResponse($"{result.Status.ToString().ToLowerInvariant()}: {result.Message}", data)
                : new SuccessResponse(string.IsNullOrEmpty(result.Message) ? "Выполнено." : result.Message, data);
        }

        /// <summary>
        /// Живой реестр из контейнера любой сцены. Скоупов может быть несколько (корневой плюс
        /// сценные), поэтому спрашиваем каждый, пока кто-то не ответит.
        /// </summary>
        private static bool TryFindRegistry(out DevCommandRegistry registry, out string failure)
        {
            registry = null;
            failure = null;

            List<LifetimeScope> scopes = UnityEngine.Object
                .FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include)
                .ToList();

            if (scopes.Count == 0)
            {
                failure = "no_scope: в сцене нет ни одного LifetimeScope — контейнер не собран.";
                return false;
            }

            foreach (LifetimeScope scope in scopes)
            {
                if (scope == null || scope.Container == null) continue;

                // Resolve в try, а НЕ TryResolve: перегрузка TryResolve в этой версии VContainer требует
                // ключ, и обобщённого варианта без него нет (та же готча описана в MapDevCommands).
                // Промах регистрации здесь не ошибка — просто скоуп оказался не тот.
                DevCommandRegistry found;
                try { found = scope.Container.Resolve(typeof(DevCommandRegistry)) as DevCommandRegistry; }
                catch { continue; }

                if (found == null) continue;

                registry = found;
                return true;
            }

            failure = $"no_registry: DevCommandRegistry не нашёлся ни в одном из {scopes.Count} " +
                      "скоупов сцены.";
            return false;
        }
    }
}
#endif
