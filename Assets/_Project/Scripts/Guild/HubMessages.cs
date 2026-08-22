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

        /// <summary>
        /// Номер акта (1..N) и ступень, на которой стоит забег, — чтобы двор говорил, ГДЕ мы, а не
        /// «IN PROGRESS».
        /// </summary>
        /// <remarks>
        /// Заказ Макса 22.08.2026: «И не понятно вообще что за In Progress или "Забег идет". Скорее
        /// пиши прям стадию забега - Акт I - Его название, Уровень 8».
        /// </remarks>
        public readonly int ActNumber;

        /// <summary>Ступень маршрута (этаж карты, 1 = вход в акт).</summary>
        public readonly int Level;

        /// <summary>Ключ имени акта; пусто — имени ещё нет, и двор скажет просто «Акт I».</summary>
        public readonly string ActTitleKey;

        /// <summary>
        /// Уйти со двора: у хозяина — бросить забег и вернуться в главное меню, у гостя — покинуть
        /// чужой сеанс. Ролям это разводит <c>IRunControl</c>, двору знать о разнице нечего.
        /// </summary>
        public readonly Action OnLeave;

        public OpenHubRequest(string guildName, Action onStartRun, CancellationToken cancellation = default,
                              int actNumber = 0, int level = 0, string actTitleKey = null,
                              Action onLeave = null)
        {
            GuildName    = guildName;
            OnStartRun   = onStartRun;
            Cancellation = cancellation;
            ActNumber    = actNumber;
            Level        = level;
            ActTitleKey  = actTitleKey;
            OnLeave      = onLeave;
        }
    }
}
