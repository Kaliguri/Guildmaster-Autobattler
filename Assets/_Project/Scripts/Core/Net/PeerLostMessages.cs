using System;
using System.Collections.Generic;

namespace Guildmaster.Core.Net
{
    /// <summary>Один вариант ответа в диалоге разрыва: что написано на кнопке и что она делает.</summary>
    public readonly struct PeerLostOption
    {
        /// <summary>Ключ локализации подписи.</summary>
        public readonly string LocKey;

        /// <summary>Подпись, пока ключа нет в таблице.</summary>
        public readonly string Fallback;

        /// <summary>Что произойдёт по нажатию.</summary>
        public readonly Action Act;

        /// <summary>Выделить как основной ответ — им же срабатывает Enter.</summary>
        public readonly bool Primary;

        public PeerLostOption(string locKey, string fallback, Action act, bool primary = false)
        {
            LocKey   = locKey;
            Fallback = fallback;
            Act      = act;
            Primary  = primary;
        }
    }

    /// <summary>
    /// Связь с напарником оборвалась — показать это игроку и дать выбор.
    /// </summary>
    /// <remarks>
    /// <b>Заведено потому, что разрыв был молчаливым.</b> У гостя отвал хоста уводил его в главное меню
    /// без единого слова — экран просто менялся, и отличить «хост вышел» от собственного сбоя было
    /// нечем. У хоста уход гостя не порождал вообще ничего: он оставался в игре, не зная, что остался
    /// один (наход. Макса 04.08.2026).
    /// <para><b>Варианты приходят от того, кто знает роль,</b> а не выводятся здесь: у хоста это
    /// «продолжить / пригласить / в меню», у гостя — «в меню / присоединиться». Экран же один, иначе
    /// два почти одинаковых разошлись бы на первой правке.</para>
    /// <para><b>Имя пропавшего обязательно.</b> «Игрок отключился» в игре на двоих читается как «кто-то
    /// из вас», а в игре на четверых — как загадка; имя приходит из состава сеанса.</para>
    /// </remarks>
    public readonly struct PeerLostRequest
    {
        /// <summary>Заголовок: кого потеряли.</summary>
        public readonly string Title;

        /// <summary>Что это значит для игры.</summary>
        public readonly string Body;

        /// <summary>Что стало с забегом. Пусто — строки не будет.</summary>
        public readonly string Consequence;

        /// <summary>Ответы, слева направо.</summary>
        public readonly IReadOnlyList<PeerLostOption> Options;

        public PeerLostRequest(string title, string body, string consequence,
                               IReadOnlyList<PeerLostOption> options)
        {
            Title       = title;
            Body        = body;
            Consequence = consequence;
            Options     = options;
        }
    }
}
