using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Тестовый <see cref="ISaveService"/> в памяти: держит значения по ссылке, поэтому ни сериализацию,
    /// ни версии схем не проверяет — это задача <c>JsonFileSaveServiceTests</c>. Здесь нужен лишь шов,
    /// чтобы сервисы забега работали без диска.
    /// <para>Один общий двойник вместо шести копий <c>MemSave</c>/<c>FakeSave</c>, разъезжавшихся по
    /// тестовым файлам: добавление члена в интерфейс правилось шесть раз.</para>
    /// </summary>
    public sealed class InMemorySaveService : ISaveService
    {
        private readonly Dictionary<string, object> _store = new();

        public void Save<T>(string key, T value) => _store[key] = value;

        public SaveLoadResult<T> TryLoad<T>(string key) =>
            _store.TryGetValue(key, out object value) && value is T typed
                ? SaveLoadResult<T>.Ok(typed, SaveSchema.VersionOf<T>(), "test")
                : SaveLoadResult<T>.Missing();

        public bool Exists(string key) => _store.ContainsKey(key);

        public void Delete(string key) => _store.Remove(key);

        public IReadOnlyList<string> List(string prefix)
        {
            var keys = new List<string>();
            foreach (string key in _store.Keys)
            {
                if (string.IsNullOrEmpty(prefix)) { keys.Add(key); continue; }
                if (key.StartsWith(prefix + "/", StringComparison.Ordinal))
                    keys.Add(key.Substring(prefix.Length + 1).Split('/')[0]);
            }
            return keys;
        }

        public void Clear() => _store.Clear();

        /// <summary>Подложить сырое значение, как будто его записала другая версия игры.</summary>
        public void Seed<T>(string key, T value) => _store[key] = value;
    }
}
