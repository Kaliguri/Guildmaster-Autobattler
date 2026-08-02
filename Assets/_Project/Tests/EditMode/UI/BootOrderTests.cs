using Guildmaster.Game;
using Guildmaster.UI;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Кто первым в кадре: тот, кто подписывается на запросы экранов, или тот, кто их шлёт.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт между двумя файлами в разных сборках, поэтому комментарием его держать нельзя:
    /// его нарушит тот, кто ни одного из них не читал. С 03.08.2026 бут-экран накрывает загрузку мира и
    /// публикуется в ПЕРВОМ кадре — до этого он уходил кадры спустя, и порядок не значил ничего.
    /// Публикация MessagePipe без подписчика — пустая операция без ошибки, поэтому нарушение выглядит не
    /// как исключение, а как игра, вставшая на чёрном экране. Причём через раз: порядок объектов в сцене
    /// не гарантирован ничем.
    /// </remarks>
    public sealed class BootOrderTests
    {
        [Test]
        public void UiRoot_Runs_Before_GameBootstrap()
        {
            int ui   = ExecutionOrderOf(typeof(UiRootBootstrap));
            int boot = ExecutionOrderOf(typeof(GameBootstrap));

            Assert.Less(ui, boot,
                $"UiRootBootstrap ({ui}) обязан исполняться раньше GameBootstrap ({boot}): первый " +
                "подписывается на запрос бут-экрана, второй его шлёт в первом же кадре.");
        }

        private static int ExecutionOrderOf(System.Type type)
        {
            object[] attrs = type.GetCustomAttributes(typeof(DefaultExecutionOrder), inherit: false);
            return attrs.Length > 0 ? ((DefaultExecutionOrder)attrs[0]).order : 0;
        }
    }
}
