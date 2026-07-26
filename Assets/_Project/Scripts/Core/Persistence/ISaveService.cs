using System.Collections.Generic;

namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Шов персистентности (ТЗ [[save-system]]): сохранение/загрузка сериализуемых DTO по строковому ключу.
    /// Единственная точка между игрой и диском — реализация прячет бэкенд (JSON-файл под
    /// <c>persistentDataPath/Saves</c>, наш собственный: реш. 2026-07-26, Easy Save остаётся референсом).
    /// Шов сохраняем: за ним же встанет Steam Cloud и, если понадобится, другой формат.
    /// <para><b>Ключ — это путь</b> внутри дерева сохранений: <c>prefs</c>,
    /// <c>profiles/{profileId}/profile</c>, <c>profiles/{profileId}/guilds/{guildId}/run</c>. Разделитель —
    /// прямой слэш, расширение добавляет реализация.</para>
    /// <para>Значение <typeparamref name="T"/> — plain <c>[Serializable]</c> DTO (публичные поля, строковые
    /// content id, без SO-ссылок), версия схемы объявляется атрибутом <see cref="SaveSchemaAttribute"/>.
    /// Не класть сюда рантайм-состояние.</para>
    /// </summary>
    public interface ISaveService
    {
        /// <summary>Сохранить значение под ключом (перезаписывает). Версия схемы берётся из типа.</summary>
        void Save<T>(string key, T value);

        /// <summary>
        /// Загрузить значение по ключу. Возвращает <b>исход</b>, а не голое значение: «нет сейва» и
        /// «сейв из более новой версии игры» требуют разной реакции, и молчаливый <c>default</c> их
        /// склеивал бы — с риском затереть чужой прогресс следующим автосейвом.
        /// </summary>
        SaveLoadResult<T> TryLoad<T>(string key);

        /// <summary>Есть ли файл под ключом. Дешёвая проверка наличия — о пригодности отвечает <see cref="TryLoad{T}"/>.</summary>
        bool Exists(string key);

        /// <summary>Удалить сохранение под ключом (no-op, если нет). Бэкап удаляется вместе с ним.</summary>
        void Delete(string key);

        /// <summary>
        /// Ключи первого уровня под указанным префиксом — без него не собрать список профилей и гильдий.
        /// Префикс <c>profiles</c> вернёт идентификаторы профилей. Порядок не гарантирован.
        /// </summary>
        IReadOnlyList<string> List(string prefix);
    }
}
