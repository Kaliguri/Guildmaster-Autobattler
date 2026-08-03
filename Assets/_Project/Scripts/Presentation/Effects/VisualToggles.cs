using System;
using System.Collections.Generic;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// ЕДИНОЕ место, где можно погасить или вернуть любой визуальный эффект — чтобы сравнить «с ним и без».
    /// Эффекты регистрируются здесь сами, а дев-команды и будущие настройки игры дёргают их по имени.
    /// </summary>
    /// <remarks>
    /// Почему реестр, а не экран с галочками: экран — это вёрстка, локализация и поддержка на каждый новый
    /// эффект. Реестр даёт то же самое одной строкой регистрации, а UI появится позже и по другому поводу:
    /// часть тумблеров (bloom, зерно, тряска) в итоге уедет игроку в настройки как ДОСТУПНОСТЬ, и вот там
    /// у них будет настоящий экран. Строить его сейчас значило бы построить дважды.
    /// </remarks>
    public sealed class VisualToggles
    {
        /// <summary>Один переключаемый эффект.</summary>
        public sealed class Entry
        {
            public string Id { get; }
            public string Description { get; }
            public bool Enabled { get; internal set; }

            private readonly Action<bool> _apply;

            internal Entry(string id, string description, bool enabled, Action<bool> apply)
            {
                Id = id;
                Description = description;
                Enabled = enabled;
                _apply = apply;
            }

            internal void Apply() => _apply?.Invoke(Enabled);
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(16);

        /// <summary>Все известные эффекты — для списка в консоли.</summary>
        public IReadOnlyCollection<Entry> All => _entries.Values;

        /// <summary>
        /// Зарегистрировать эффект. Повторная регистрация с тем же id ПЕРЕЗАПИСЫВАЕТ обработчик, но
        /// сохраняет текущее состояние: после перезапуска сцены объект новый, а выбор игрока — прежний.
        /// </summary>
        public void Register(string id, string description, Action<bool> apply, bool defaultEnabled = true)
        {
            if (string.IsNullOrEmpty(id) || apply == null) return;

            bool enabled = _entries.TryGetValue(id, out Entry existing) ? existing.Enabled : defaultEnabled;
            var entry = new Entry(id, description, enabled, apply);
            _entries[id] = entry;
            entry.Apply();
        }

        /// <summary>Убрать эффект из реестра (объект уничтожен).</summary>
        public void Unregister(string id)
        {
            if (!string.IsNullOrEmpty(id)) _entries.Remove(id);
        }

        /// <summary>Состояние эффекта. Неизвестный id считается включённым — «ничего не выключали».</summary>
        public bool IsEnabled(string id) => !_entries.TryGetValue(id, out Entry e) || e.Enabled;

        /// <summary>Включить/выключить. false = такого эффекта нет.</summary>
        public bool Set(string id, bool enabled)
        {
            if (!_entries.TryGetValue(id, out Entry e)) return false;
            e.Enabled = enabled;
            e.Apply();
            return true;
        }

        /// <summary>Переключить. null = такого эффекта нет.</summary>
        public bool? Toggle(string id)
        {
            if (!_entries.TryGetValue(id, out Entry e)) return null;
            Set(id, !e.Enabled);
            return e.Enabled;
        }

        /// <summary>Вернуть всё как было по умолчанию.</summary>
        public void EnableAll()
        {
            foreach (Entry e in _entries.Values) { e.Enabled = true; e.Apply(); }
        }
    }
}
