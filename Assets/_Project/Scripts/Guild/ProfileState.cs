using System;
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
    /// <para><b>Что здесь пока пусто.</b> Открытия, Судьбы, прегены и статистика — состав решается
    /// дизайном меты и появится полями сюда. Поэтому схема нарочно минимальна: класть заглушки, которые
    /// потом придётся мигрировать, дороже, чем добавить поле (добавление бампа не требует, §5).</para>
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
    }

    /// <summary>
    /// Гильдия — дом игрока и <b>одновременно слот сохранения</b> (ТЗ [[save-system]] §3): отдельной
    /// сущности «слот» нет, игрок выбирает не «Save 1», а дом. Внутри гильдии не более одного активного
    /// забега.
    /// <para>Хранится в <c>profiles/{profileId}/guilds/{Id}/guild</c>, рядом с <c>run</c>.</para>
    /// <para><b>Что здесь пока пусто.</b> Летопись подвигов и Сосуды (люди, переносимые между забегами)
    /// — состав за дизайном меты; появятся полями сюда.</para>
    /// </summary>
    [Serializable]
    [SaveSchema(1)]
    public sealed class GuildState
    {
        public string Id = string.Empty;

        /// <summary>Имя дома, которое видит игрок.</summary>
        public string Name = string.Empty;

        public string CreatedUtc = string.Empty;
    }
}
