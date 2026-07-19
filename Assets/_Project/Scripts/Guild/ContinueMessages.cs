using System;
using System.Threading;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать единую кнопку «Продолжить» (план [[act-map-run-loop]] §4 A4) — консистентный «бит»
    /// между разрешённым узлом и возвратом на карту. Публикует петля акта (<c>ContinuePresenter</c>), слушает
    /// UI (<c>MenuRouter</c> рисует кнопку в правом нижнем углу). Ровно один вызов <see cref="OnContinue"/>.
    /// </summary>
    public readonly struct OpenContinueRequest
    {
        /// <summary>Лок-ключ подписи кнопки (пусто → дефолт «Продолжить»).</summary>
        public readonly string LabelKey;

        /// <summary>Колбэк нажатия (ровно один вызов) — петля продвигается и открывает карту.</summary>
        public readonly Action OnContinue;

        /// <summary>Токен отмены забега (QA #37): отмена закрывает «Продолжить» через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        public OpenContinueRequest(string labelKey, Action onContinue, CancellationToken cancellation = default)
        {
            LabelKey     = labelKey;
            OnContinue   = onContinue;
            Cancellation = cancellation;
        }
    }
}
