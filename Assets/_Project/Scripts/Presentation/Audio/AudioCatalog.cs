using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Каталог соответствий «ключ звука → FMOD-событие» (вики «13» §3.5, §7). Точные записи
    /// (<c>{contentId}.{action}</c>) и дефолты по действию. Живёт в Presentation; наполняется FMOD-контентом
    /// в Фазе 7/9 — сейчас <see cref="EventReference"/> могут быть пустыми, звук не играет (заглушка
    /// <c>IAudioService</c>). Резолвер видит каталог через <see cref="IAudioCatalog"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Audio/Audio Catalog", fileName = "AudioCatalog")]
    public sealed class AudioCatalog : ScriptableObject, IAudioCatalog
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Точный ключ: {contentId}.{action}, напр. relic.defender.attack.")]
            public string Key;
            public EventReference Event;
        }

        [Serializable]
        public struct ActionDefault
        {
            public AudioAction Action;
            public EventReference Event;
        }

        [Tooltip("Точные соответствия ключ→событие. Ключ перекрывает дефолт действия.")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        [Tooltip("Дефолт по действию: играет, когда точной записи нет. Должны покрывать весь AudioAction.")]
        [SerializeField] private ActionDefault[] _defaults = Array.Empty<ActionDefault>();

        public IReadOnlyList<Entry> Entries => _entries;
        public IReadOnlyList<ActionDefault> Defaults => _defaults;

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Key == key) return true;
            return false;
        }

        /// <summary>
        /// Играет ли что-нибудь по этому ключу: есть точная запись или дефолт действия, и ссылка не пуста.
        /// Обёртка над <see cref="TryGetEvent"/> для тех, кому нельзя видеть FMOD-типы (тесты, валидаторы) —
        /// <see cref="EventReference"/> живёт в сборке FMODUnity, и тащить её в каждую сборку-потребителя ради
        /// одной проверки незачем.
        /// </summary>
        public bool HasSound(string key) => TryGetEvent(key, out _);

        /// <summary>Ключи записей, за которыми нет FMOD-события (в игре это тихий no-op).</summary>
        public IEnumerable<string> KeysWithoutEvent()
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Event.IsNull) yield return _entries[i].Key;
        }

        /// <summary>Ключи всех точных записей — без доступа к их FMOD-ссылкам.</summary>
        public IEnumerable<string> EntryKeys()
        {
            for (int i = 0; i < _entries.Length; i++) yield return _entries[i].Key;
        }

        public bool HasDefault(AudioAction action)
        {
            for (int i = 0; i < _defaults.Length; i++)
                if (_defaults[i].Action == action) return true;
            return false;
        }

        /// <summary>
        /// Резолвнутый ключ (<see cref="AudioResolver.Resolve"/>) → FMOD-событие: точная запись
        /// (<c>{contentId}.{action}</c>) либо дефолт действия (строка действия, напр. <c>hit</c>).
        /// Возвращает <c>false</c>, если события нет ИЛИ ссылка пустая (нет банка) — вызывающий молчит.
        /// </summary>
#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: перезаписать содержимое каталога (наполнение из FMOD-манифеста, вики impl «09» §П5).
        /// Не для рантайма — только инструмент AudioCatalogPopulator. Вызывающий сам делает SetDirty/SaveAssets.
        /// </summary>
        public void EditorSetContents(Entry[] entries, ActionDefault[] defaults)
        {
            _entries = entries ?? Array.Empty<Entry>();
            _defaults = defaults ?? Array.Empty<ActionDefault>();
        }
#endif

        public bool TryGetEvent(string key, out EventReference evt)
        {
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < _entries.Length; i++)
                    if (_entries[i].Key == key) { evt = _entries[i].Event; return !evt.IsNull; }

                for (int i = 0; i < _defaults.Length; i++)
                    if (AudioResolver.ActionKey(_defaults[i].Action) == key) { evt = _defaults[i].Event; return !evt.IsNull; }
            }
            evt = default;
            return false;
        }
    }
}
