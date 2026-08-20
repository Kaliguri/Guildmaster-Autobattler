using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Профиль аккаунта — мета ИГРОКА, переживающая забеги и гильдии (ТЗ [[save-system]] §3).
    /// Переключаемый: отдельный профиль под соло и отдельный под игры с друзьями, чтобы контент можно
    /// было открывать заново осознанно.
    /// <para>Хранится в <c>profiles/{Id}/profile</c>. Внутри профиля живут гильдии, внутри гильдии —
    /// забег. В кооперативе профиль у каждого игрока свой: гильдия у хоста, а открытия капают каждому
    /// (реш. Макса 2026-07-26).</para>
    /// <para><b>Здесь лежит то, что открыл ИГРОК, а не заработал дом</b> (реш. 2026-07-27/12): контент
    /// открывается вехами и деньгами не покупается, поэтому за валюту гильдии сюда не попадает ничего.
    /// Хозяйство дома — в <see cref="GuildState"/>.</para>
    /// </summary>
    [Serializable]
    [SaveSchema(1)]
    public sealed class ProfileState
    {
        /// <summary>Локальный идентификатор. <b>Не SteamID:</b> смена аккаунта не должна стирать прогресс.</summary>
        public string Id = string.Empty;

        /// <summary>Имя профиля, которое видит игрок.</summary>
        public string Name = string.Empty;

        /// <summary>Когда заведён (ISO-8601 UTC). Для сортировки списка и диагностики.</summary>
        public string CreatedUtc = string.Empty;

        /// <summary>Гильдия, в которой играли в прошлый раз — чтобы «Продолжить» не спрашивал лишнего.</summary>
        public string LastGuildId = string.Empty;

        /// <summary>
        /// Ник для игры, если игрок захотел свой. Пусто и <see cref="UseSteamName"/> — берём имя из Steam.
        /// </summary>
        /// <remarks>
        /// <b>Идентичность живёт в профиле, а настройки клиента — нет</b> (уточнение к ТЗ сейвов,
        /// 03.08.2026). Громкость и разрешение принадлежат машине и не должны прыгать при смене профиля;
        /// ник, цвет и курсор — наоборот, это и есть «кем я играю», и профиль ровно за это отвечает.
        /// </remarks>
        public string DisplayName = string.Empty;

        /// <summary>Брать ник из Steam (иначе играет <see cref="DisplayName"/>).</summary>
        public bool UseSteamName = true;

        /// <summary>
        /// Мейн-цвет игрока — место в наборе цветов палитры. Им идут курсор, подпись имени и метки:
        /// в коопе это опознание «чьё это» без чтения текста.
        /// </summary>
        public int ColorIndex;

        /// <summary>Выбранный скин курсора (<c>cursor.*</c>). Пусто — умолчание набора.</summary>
        public string CursorSkinId = string.Empty;

        /// <summary>Открытые прегены: доступны любому дому этого профиля (реш. 2026-07-27/14-15).</summary>
        public List<string> UnlockedPregenIds = new();

        /// <summary>Открытые Судьбы. Сама Судьба берётся квестом внутри забега, но право её брать — здесь.</summary>
        public List<string> UnlockedFateIds = new();

        /// <summary>Открытые Капитаны — такое же открытие игрока, как Судьба или преген.</summary>
        public List<string> UnlockedCaptainIds = new();

        /// <summary>
        /// Наибольшая открытая ступень возвышения. <b>Доступ живёт здесь, а текущая ступень — у дома</b>
        /// (реш. 2026-07-27/5): ветеран не обязан ползти снизу на каждом новом доме, но мрачность не
        /// включается в доме, который её не прожил.
        /// </summary>
        public int MaxAscensionUnlocked;

        /// <summary>Что игрок уже встречал — основа компендиума (id реликвий, врагов, эффектов).</summary>
        public List<string> CompendiumSeenIds = new();

        /// <summary>
        /// Открытые записки «Из мастерской» — геймдев-кухня во врезке карточки компендиума
        /// (реш. 2026-07-27/24). Открывается вехой по той же сущности, к которой привязана.
        /// </summary>
        public List<string> UnlockedDevNoteIds = new();
    }

    /// <summary>
    /// Гильдия — дом игрока и <b>одновременно слот сохранения</b> (ТЗ [[save-system]] §3): отдельной
    /// сущности «слот» нет, игрок выбирает не «Save 1», а дом. Внутри гильдии не более одного активного
    /// забега.
    /// <para>Хранится в <c>profiles/{profileId}/guilds/{Id}/guild</c>, рядом с <c>run</c> и <c>book</c>.
    /// Память дома вынесена в <see cref="GuildBook"/> отдельным ключом: ростер читается на каждом экране
    /// казарм, а история — редко.</para>
    /// <para><b>Здесь хозяйство ДОМА</b> — то, что растёт за валюту гильдии (реш. 2026-07-27/12-13).
    /// Открытия игрока (прегены, Судьбы, Капитаны) сюда не попадают: они в <see cref="ProfileState"/>.</para>
    /// </summary>
    [Serializable]
    [SaveSchema(1)]
    public sealed class GuildState
    {
        public string Id = string.Empty;

        /// <summary>Имя дома, которое видит игрок.</summary>
        public string Name = string.Empty;

        public string CreatedUtc = string.Empty;

        /// <summary>
        /// Казна дома. <b>Принадлежит гильдии, а не игроку</b>, и в коопе тратить может каждый участник
        /// без гейта прав (реш. 2026-07-27/10). Не путать с золотом забега: то сгорает в конце похода,
        /// эта копится (реш. 2026-07-27/11).
        /// </summary>
        public int Currency;

        /// <summary>
        /// Сколько людей помещается в доме сейчас. Стартует с <c>GameConfig.StartingRosterCapacity</c> (8)
        /// и растёт за валюту до <c>GameConfig.MaxRosterCapacity</c> (64) — реш. 2026-07-27/3.
        /// </summary>
        public int RosterCapacity;

        /// <summary>Живые люди дома. Павшие переезжают в <see cref="GuildBook.Fallen"/> целиком.</summary>
        public List<VesselState> Roster = new();

        /// <summary>Ступень возвышения, на которой дом играет сейчас. От неё зависят смертность и тон.</summary>
        public int Ascension;

        /// <summary>Наибольшая ступень, взятая ЭТИМ домом. Право её брать приходит из профиля.</summary>
        public int MaxAscensionReached;

        /// <summary>
        /// Сколько ветеранов дом потерял за всё время. По достижении
        /// <c>GameConfig.VeteranHireUnlockDeaths</c> открывается платный наём готовых ветеранов — чтобы
        /// после плохой ночи вершина не превращалась в отработку часов (реш. 2026-07-27/7).
        /// </summary>
        public int VeteranDeaths;

        /// <summary>Купленные прокачки дома: id улучшения → взятый уровень (слоты, баны пула, набор, удобства).</summary>
        public Dictionary<string, int> Upgrades = new();

        /// <summary>Что этот дом уже прошёл: кампания и ступень, на которой её взяли.</summary>
        public List<CampaignRecord> CompletedCampaigns = new();
    }

    /// <summary>Пройденная кампания дома: что взято и на какой ступени возвышения (реш. 2026-07-27/1).</summary>
    [Serializable]
    public sealed class CampaignRecord
    {
        public string CampaignId = string.Empty;

        /// <summary>Наибольшая ступень, на которой эта кампания была пройдена этим домом.</summary>
        public int BestAscension;
    }
}
