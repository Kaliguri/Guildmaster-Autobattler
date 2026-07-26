using System;
using System.Collections.Generic;

namespace Guildmaster.Core.Persistence
{
    /// <summary>Профиль в списке выбора: то, что нужно показать, без загрузки его содержимого.</summary>
    public readonly struct ProfileSummary
    {
        public string Id   { get; }
        public string Name { get; }

        public ProfileSummary(string id, string name)
        {
            Id   = id;
            Name = name;
        }
    }

    /// <summary>
    /// Владелец иерархии сохранений: <b>профиль → гильдии → забег</b> (ТЗ [[save-system]] §3).
    /// <para>Профиль — мета аккаунта, переключаемая (соло / игры с друзьями). Гильдия — дом и
    /// <b>одновременно слот сохранения</b>: отдельной сущности «слот» в игре нет, игрок выбирает дом.
    /// В гильдии не более одного активного забега.</para>
    /// <para>Сервис отвечает и на вопрос «куда писать забег» — ключ зависит от того, какие профиль и
    /// гильдия сейчас активны, поэтому <see cref="RunKey"/> живёт здесь, а не в сервисе забега.</para>
    /// </summary>
    public interface IProfileService
    {
        IReadOnlyList<ProfileSummary> Profiles { get; }

        /// <summary>Активный профиль (пустой Id, если ни одного ещё нет).</summary>
        ProfileSummary ActiveProfile { get; }

        /// <summary>Гильдии активного профиля.</summary>
        IReadOnlyList<ProfileSummary> Guilds { get; }

        /// <summary>Активная гильдия — она же текущий слот сохранения.</summary>
        ProfileSummary ActiveGuild { get; }

        /// <summary>Достигнут ли лимит профилей (<c>GameConfig.MaxProfiles</c>).</summary>
        bool ProfilesFull { get; }

        /// <summary>Достигнут ли лимит гильдий в активном профиле (<c>GameConfig.MaxGuildsPerProfile</c>).</summary>
        bool GuildsFull { get; }

        /// <summary>
        /// Ключ сейва забега активной гильдии (<c>profiles/{p}/guilds/{g}/run</c>). Пустая строка, если
        /// активной гильдии нет — писать забег в этом случае некуда, и вызывающий обязан это заметить.
        /// </summary>
        string RunKey { get; }

        /// <summary>Создать профиль и сделать активным. Null, если достигнут лимит.</summary>
        ProfileSummary? CreateProfile(string name);

        /// <summary>Переключиться на профиль. false = профиля нет.</summary>
        bool SelectProfile(string profileId);

        /// <summary>Удалить профиль вместе со всеми его гильдиями и забегами. Необратимо.</summary>
        bool DeleteProfile(string profileId);

        /// <summary>Создать гильдию в активном профиле и сделать активной. Null, если лимит или нет профиля.</summary>
        ProfileSummary? CreateGuild(string name);

        /// <summary>Переключиться на гильдию активного профиля. false = гильдии нет.</summary>
        bool SelectGuild(string guildId);

        /// <summary>Удалить гильдию вместе с её забегом. Необратимо: это месяцы Летописи.</summary>
        bool DeleteGuild(string guildId);

        /// <summary>Поднимается при смене активного профиля/гильдии и при изменении их списков.</summary>
        event Action Changed;
    }
}
