using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать ПРОЩАНИЕ узла: последний кадр пройденного узла, который держит экран, пока игрок
    /// не пошёл дальше. Публикует флоу узла (магазин/сундук/привал), рисует UI (<c>MenuRouter</c>).
    /// </summary>
    /// <remarks>
    /// Единый ритм конца ЛЮБОГО узла (реш. Макса 2026-07-26, QA #48/#49): узел не сваливает игрока в мир,
    /// а сворачивается в кадр с текстом — «что произошло». Поверх этого кадра петля кладёт кнопки бита
    /// («Продолжить» / «К построению»), и они единственный выход. У текстового ивента такой кадр уже свой
    /// (панель ивента с текстом выбранного варианта), поэтому он этот запрос не шлёт — форма кадра одна,
    /// а текст у каждого узла свой.
    /// <para>Живёт по <see cref="Cancellation"/> — токену УЗЛА (<c>RunContext.NodeCancellation</c>): кадр
    /// гаснет, только когда игрок вошёл в следующий узел.</para>
    /// </remarks>
    public readonly struct OpenNodeFarewellRequest
    {
        /// <summary>Лок-ключ заголовка кадра (таблица UI).</summary>
        public readonly string TitleKey;

        /// <summary>Лок-ключ текста прощания (таблица UI).</summary>
        public readonly string BodyKey;

        /// <summary>Токен жизни узла: снимает кадр на входе в следующий узел (или при отмене забега).</summary>
        public readonly CancellationToken Cancellation;

        public OpenNodeFarewellRequest(string titleKey, string bodyKey, CancellationToken cancellation = default)
        {
            TitleKey     = titleKey;
            BodyKey      = bodyKey;
            Cancellation = cancellation;
        }
    }
}
