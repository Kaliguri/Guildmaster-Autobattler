namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Шов персистентности (вики «2», «13» §1): сохранение/загрузка сериализуемых DTO по строковому ключу.
    /// Единственная точка между игрой и диском — реализация прячет бэкенд (JSON-файл в persistentDataPath,
    /// наш собственный: реш. 2026-07-26, Easy Save остаётся референсом, а не будущей реализацией).
    /// Шов сохраняем: за ним же встанет Steam Cloud и, если понадобится, другой формат.
    /// <para>Значение <typeparamref name="T"/> — plain <c>[Serializable]</c> DTO (публичные поля, строковые
    /// content id, без SO-ссылок), сериализуется движком. Не класть сюда рантайм-состояние.</para>
    /// </summary>
    public interface ISaveService
    {
        /// <summary>Сохранить значение под ключом (перезаписывает).</summary>
        void Save<T>(string key, T value);

        /// <summary>Загрузить значение по ключу; <c>default</c> (null), если ключа нет.</summary>
        T Load<T>(string key);

        /// <summary>Есть ли сохранение под ключом.</summary>
        bool Exists(string key);

        /// <summary>Удалить сохранение под ключом (no-op, если нет).</summary>
        void Delete(string key);
    }
}
