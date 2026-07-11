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

        public bool HasDefault(AudioAction action)
        {
            for (int i = 0; i < _defaults.Length; i++)
                if (_defaults[i].Action == action) return true;
            return false;
        }
    }
}
