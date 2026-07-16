namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Шов персистентности (вики «2», «13» §1): сохранение/загрузка сериализуемых DTO по строковому ключу.
    /// Единственная точка между игрой и диском — реализация прячет бэкенд (сейчас JSON-файл в
    /// persistentDataPath; ES3 + Steam Cloud — отложенная замена за этим же интерфейсом, seams-vs-defer).
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
