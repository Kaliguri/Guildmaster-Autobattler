using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Книга гильдии — вся память дома: <b>Летопись</b> (подвиги людей), <b>Хроника</b> (лента забегов)
    /// и <b>Мемориал</b> (павшие). ГДД — [[gdd/30-run-meta/guild-development|Развитие гильдии]] §Память дома.
    /// <para><b>Отдельный ключ</b> <c>profiles/{p}/guilds/{g}/book</c>, а не поле в
    /// <see cref="GuildState"/>: живой ростер читается на каждом экране казарм, а история — редко, и
    /// тащить её с диска каждый раз незачем.</para>
    /// <para><b>Кап и сжатие запрещены.</b> Записываются ВСЕ «Сосуды», включая погибших, полными
    /// карточками (реш. Макса 2026-07-27/19). Файл растёт линейно от числа забегов — это принято
    /// осознанно: несколько мегабайт текста ничего не стоят, а урезанная биография обесценивает
    /// потерю, ради которой всё и затевалось.</para>
    /// </summary>
    [Serializable]
    [SaveSchema(1)]
    public sealed class GuildBook
    {
        /// <summary>Летопись: подвиги всех людей дома, живых и павших.</summary>
        public List<ChronicleEntry> Chronicle = new();

        /// <summary>Хроника: по записи на каждый завершённый забег, в порядке от старых к новым.</summary>
        public List<RunRecord> Runs = new();

        /// <summary>Мемориал: полные карточки тех, кто не вернулся. Не усекаются.</summary>
        public List<VesselState> Fallen = new();
    }

    /// <summary>
    /// Одна запись Летописи: подвиг + <b>конкретное число</b> + «кем был тогда» (реш. 2026-07-15/30).
    /// Число цитируется точное, но флажок «это подвиг» вешается на относительную значимость — личный
    /// рекорд или редкий порог, — иначе при скейле ×10 герой хвастается будничным статом.
    /// </summary>
    [Serializable]
    public sealed class ChronicleEntry
    {
        /// <summary>Чей подвиг: <see cref="VesselState.Id"/>. Живёт и после гибели человека.</summary>
        public string VesselId = string.Empty;

        /// <summary>Что за подвиг: id типа (трипл-килл, личный рекорд по стату, победа вопреки невзгодам).</summary>
        public string DeedId = string.Empty;

        /// <summary>Само число подвига — то, что барк цитирует дословно («защитился от 32 000 урона»).</summary>
        public double Value;

        /// <summary>Кем он был в тот момент: id Мементо — снапшот архетипа, а не текущая роль.</summary>
        public string RelicId = string.Empty;

        /// <summary>В каком забеге это случилось: индекс в <see cref="GuildBook.Runs"/>, −1 если вне забега.</summary>
        public int RunIndex = -1;
    }

    /// <summary>
    /// Строка Хроники — один прожитый забег дома: чем он был, чем кончился и кого стоил
    /// (реш. 2026-07-27/20).
    /// </summary>
    [Serializable]
    public sealed class RunRecord
    {
        /// <summary>Какая кампания. Кампания = уникальный забег со своими ивентами и врагами (2026-07-27/1).</summary>
        public string CampaignId = string.Empty;

        /// <summary>Ступень возвышения, на которой шли. От неё зависела и смертность людей.</summary>
        public int Ascension;

        /// <summary>Дошли ли до конца кампании.</summary>
        public bool Victory;

        /// <summary>Когда закончился (ISO-8601 UTC).</summary>
        public string FinishedUtc = string.Empty;

        /// <summary>Кто не вернулся: <see cref="VesselState.Id"/> павших в этом забеге.</summary>
        public List<string> FallenVesselIds = new();

        /// <summary>Id гильдии, у которой гостевали, если это был вечер в чужом доме (реш. 2026-07-27/23).</summary>
        public string GuestOfGuildName = string.Empty;
    }
}
