using System;
using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать Двор гильдии — дом, из которого группа уходит в забег.
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

        /// <summary>
        /// Отдать свой голос за выход в забег. Зовётся столько раз, сколько игрок передумывает.
        /// </summary>
        /// <remarks>
        /// <b>Не «уходим», а голос</b> (вердикт Макса 08.08.2026: «Надо вообще его сделать когда кликают
        /// оба, как с готовностью»). Пока кнопка закрывала двор сама, дать её гостю было нельзя: он ушёл
        /// бы со двора один, а напарник остался бы стоять. Экран закрывается объявлением — одинаково у
        /// обеих ролей.
        /// </remarks>
        public readonly Action OnStartRun;

        /// <summary>Двор закрылся: группа сошлась и уходит в забег (у гостя — объявлением хоста).</summary>
        public readonly CancellationToken Cancellation;

        public OpenHubRequest(string guildName, Action onStartRun, CancellationToken cancellation = default)
        {
            GuildName    = guildName;
            OnStartRun   = onStartRun;
            Cancellation = cancellation;
        }
    }
}
