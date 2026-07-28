using System;
using System.Collections.Generic;

namespace Guildmaster.Core.Localization
{
    /// <summary>
    /// Единая точка локализации (вики «13» §5). Геймплей и UI берут строки только отсюда и не видят
    /// Unity.Localization: Core остаётся без зависимости от пакета, реализация в <c>Guildmaster.Game</c>
    /// оборачивает String Table-таблицы. Локаль адресуется кодом (<c>"en"</c>/<c>"ru"</c>), а не типом
    /// пакета — тот же шов, что у <see cref="Guildmaster.Core.Input.IInputService"/>.
    /// <para>Потребители (UI) подключатся в Фазе 7 — сейчас это только сервис + DI-регистрация.</para>
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>Код активной локали (<c>"en"</c>/<c>"ru"</c>); пусто, если локализация не готова.</summary>
        string CurrentLocale { get; }

        /// <summary>Коды всех доступных локалей в порядке проекта.</summary>
        IReadOnlyList<string> AvailableLocales { get; }

        /// <summary>Поднимается после смены активной локали (перерисовать подписанный UI).</summary>
        event Action LocaleChanged;

        /// <summary>Строка из таблицы <c>Content</c> по ключу (<c>{id}.name</c> / <c>{id}.desc</c>).</summary>
        string GetString(string key);

        /// <summary>Строка из указанной String Table (<c>Content</c> / <c>UI</c>) по ключу.</summary>
        string GetString(string table, string key);

        /// <summary>
        /// Строка из таблицы <c>Content</c> с подстановкой именованных аргументов
        /// (Smart String: <c>«Наносит {dmg} урона»</c>).
        /// </summary>
        string GetString(string key, IReadOnlyDictionary<string, object> args);

        /// <summary>
        /// Строка из указанной таблицы с подстановкой именованных аргументов.
        /// </summary>
        /// <remarks>
        /// Именно ИМЕНОВАННЫХ, не позиционных: описание способности содержит несколько
        /// величин, и позиционные <c>{0}</c>/<c>{1}</c> в переводе неизбежно перепутают
        /// местами — переводчик не видит, что есть что. Плюс порядок слов в разных языках
        /// разный, и привязка слота к смыслу (а не к позиции) — единственная переживающая
        /// перевод форма.
        /// </remarks>
        string GetString(string table, string key, IReadOnlyDictionary<string, object> args);

        /// <summary>Сменить активную локаль по коду; no-op, если код неизвестен.</summary>
        void SetLocale(string localeCode);
    }
}
