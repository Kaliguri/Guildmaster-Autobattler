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
    /// Кем игрок выглядит для остальных: имя, цвет и курсор.
    /// </summary>
    /// <remarks>
    /// <b>Идентичность живёт в профиле, а настройки клиента — нет</b> (решение Макса 03.08.2026).
    /// Громкость и разрешение принадлежат машине и прыгать при смене профиля не должны; ник, цвет и
    /// курсор — наоборот, это и есть «кем я играю».
    /// </remarks>
    public readonly struct ProfileIdentity
    {
        /// <summary>Свой ник для игры. Пусто вместе с <see cref="UseSteamName"/> — играет имя из Steam.</summary>
        public string DisplayName { get; }

        /// <summary>Брать имя из Steam, а не из <see cref="DisplayName"/>.</summary>
        public bool UseSteamName { get; }

        /// <summary>Мейн-цвет: место в наборе цветов палитры.</summary>
        public int ColorIndex { get; }

        /// <summary>Скин курсора (<c>cursor.*</c>); пусто — умолчание набора.</summary>
        public string CursorSkinId { get; }

        public ProfileIdentity(string displayName, bool useSteamName, int colorIndex, string cursorSkinId)
        {
            DisplayName  = displayName ?? string.Empty;
            UseSteamName = useSteamName;
            ColorIndex   = colorIndex;
            CursorSkinId = cursorSkinId ?? string.Empty;
        }

        /// <summary>Как игрока зовут в игре: свой ник, если выбран и задан, иначе имя из Steam.</summary>
        public string ResolveName(string steamName) =>
            !UseSteamName && !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : steamName;
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

        /// <summary>
        /// Есть ли профиль, в котором можно играть. <c>false</c> — игра обязана спросить игрока раньше,
        /// чем пустит его в меню: писать забег без профиля некуда.
        /// </summary>
        bool HasActiveProfile { get; }

        /// <summary>
        /// Идентичность активного профиля. Вне профиля — значения по умолчанию.
        /// </summary>
        ProfileIdentity Identity { get; }

        /// <summary>
        /// Создать профиль в свободном слоте и сделать активным. Null, если достигнут лимит.
        /// </summary>
        /// <remarks>
        /// <b>Имени профиль не получает — только номер слота</b> («Профиль 1», «Профиль 2»; решение
        /// Макса 03.08.2026). Именуемая сущность у игрока одна — дом: он и есть слот сохранения. Второе
        /// имя рядом с ним заставляло бы придумывать название тому, что игрок различает по счёту.
        /// </remarks>
        ProfileSummary? CreateProfile();

        /// <summary>Записать идентичность в активный профиль. false — профиля нет.</summary>
        bool SaveIdentity(in ProfileIdentity identity);

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
