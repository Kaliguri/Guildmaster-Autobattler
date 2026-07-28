using System;
using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать привал: несёт <see cref="Session"/> (бюджет действий и траты) — экран биндится к нему,
    /// как магазин к своему контроллеру. Публикует <c>CampFlow</c>, слушает UI. <see cref="OnLeave"/> —
    /// ровно один вызов, узел после него считается пройденным.
    /// </summary>
    public readonly struct OpenCampRequest
    {
        /// <summary>Состояние привала: сколько действий осталось и что с ними уже сделали.</summary>
        public readonly CampSession Session;

        /// <summary>Колбэк ухода с привала (ровно один вызов) — петля акта идёт дальше.</summary>
        public readonly Action OnLeave;

        /// <summary>Токен отмены забега (QA #37): отмена закрывает привал через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        public OpenCampRequest(CampSession session, Action onLeave, CancellationToken cancellation = default)
        {
            Session      = session;
            OnLeave      = onLeave;
            Cancellation = cancellation;
        }
    }
}
