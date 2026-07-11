using System;
using System.Collections.Generic;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Рантайм-индекс контента: словарь <c>id → ContentDefinition</c>, построенный один раз из
    /// <see cref="ContentDatabase"/> при сборке DI-контейнера (вики «13» §3.6). Реализация
    /// <see cref="IContentDatabase"/> — сам SO остаётся чистыми данными.
    /// </summary>
    public sealed class ContentRegistry : IContentDatabase
    {
        private readonly Dictionary<string, ContentDefinition> _byId;

        public ContentRegistry(IEnumerable<ContentDefinition> entries)
        {
            _byId = new Dictionary<string, ContentDefinition>(StringComparer.Ordinal);
            foreach (ContentDefinition def in entries)
            {
                if (def == null) continue;
                if (string.IsNullOrEmpty(def.Id))
                    throw new ArgumentException($"ContentDatabase entry '{def.name}' has an empty id.");
                if (_byId.ContainsKey(def.Id))
                    throw new ArgumentException($"Duplicate content id '{def.Id}' in ContentDatabase.");
                _byId.Add(def.Id, def);
            }
        }

        public T Get<T>(string id) where T : ContentDefinition
        {
            if (TryGet(id, out T def)) return def;
            throw new KeyNotFoundException(
                $"Content id '{id}' of type {typeof(T).Name} not found in ContentDatabase (configuration error).");
        }

        public bool TryGet<T>(string id, out T def) where T : ContentDefinition
        {
            if (id != null && _byId.TryGetValue(id, out ContentDefinition d) && d is T typed)
            {
                def = typed;
                return true;
            }
            def = null;
            return false;
        }

        public IReadOnlyList<T> All<T>() where T : ContentDefinition
        {
            var list = new List<T>();
            foreach (ContentDefinition d in _byId.Values)
                if (d is T typed) list.Add(typed);
            return list;
        }
    }
}
