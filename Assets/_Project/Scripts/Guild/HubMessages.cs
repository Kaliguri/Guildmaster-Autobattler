using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать Двор гильдии — дом, из которого игрок уходит в забег.
    /// </summary>
    /// <remarks>
    /// <b>Хаб стоит МЕЖДУ выбором дома и забегом,</b> а не вместо одного из них: дом отвечает на вопрос
    /// «чей прогресс», забег — «куда идём», и склеенные в один шаг они не оставляли места ничему, что
    /// живёт между забегами (ростер, найм, лавка двора — ГДД [[guild-hub-courtyard]]).
    /// <para>Пока это заглушка с единственной кнопкой, поэтому <see cref="OnStartRun"/> — весь его
    /// контракт. Появятся занятия двора — появятся и соседние исходы; отдельного «в главное меню» здесь
    /// намеренно нет, чтобы заглушка не притворялась готовым экраном.</para>
    /// </remarks>
    public readonly struct OpenHubRequest
    {
        /// <summary>Имя дома, в котором мы стоим, — заглушке нужно показать, чей это двор.</summary>
        public readonly string GuildName;

        /// <summary>Игрок уходит в забег (ровно один вызов).</summary>
        public readonly Action OnStartRun;

        public OpenHubRequest(string guildName, Action onStartRun)
        {
            GuildName  = guildName;
            OnStartRun = onStartRun;
        }
    }
}
