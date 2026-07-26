using System;
using System.Collections.Generic;
using System.IO;
using FMODUnity;
using Guildmaster.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Audio.Editor
{
    /// <summary>
    /// Наполняет <see cref="AudioCatalog"/> из FMOD-манифеста (вики impl «09» §П5): читает
    /// <c>FMOD Project/Scripts/manifest.json</c>, резолвит каждый event-путь через FMOD
    /// <see cref="EventManager"/> в GUID и пишет точные записи + дефолты действий в ассет.
    /// Пропущенное событие (нет в собранных банках) остаётся пустой ссылкой → тишина, не ошибка.
    /// Требует собранных банков в проекте (Alebardium/Audio после FMOD build + Refresh Banks).
    /// </summary>
    public static class AudioCatalogPopulator
    {
        private const string CatalogPath = "Assets/_Project/ScriptableObjects/Audio/AudioCatalog.asset";
        private const string ManifestRelative = "../FMOD Project/Scripts/manifest.json";

        [Serializable] private sealed class ManifestDto { public EventDto[] events; }
        [Serializable] private sealed class EventDto { public string key; public string action; public bool isDefault; public string path; }

        [MenuItem("Alebardium/Audio/Populate Catalog from Manifest", priority = 300)]
        public static void Populate()
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, ManifestRelative));
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[AudioCatalog] Манифест не найден: {manifestPath}");
                return;
            }

            ManifestDto dto = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(manifestPath));
            if (dto?.events == null || dto.events.Length == 0)
            {
                Debug.LogError("[AudioCatalog] Манифест не распарсился или пуст.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[AudioCatalog] Ассет каталога не найден: {CatalogPath}");
                return;
            }

            var entries = new List<AudioCatalog.Entry>();
            var defaultByAction = new Dictionary<AudioAction, EventReference>();
            int resolved = 0, missing = 0;

            foreach (EventDto e in dto.events)
            {
                EventReference reference = default;
                EditorEventRef er = EventManager.EventFromPath(e.path);
                if (er != null)
                {
                    reference = new EventReference { Guid = er.Guid, Path = er.Path };
                    resolved++;
                }
                else
                {
                    Debug.LogWarning($"[AudioCatalog] Нет FMOD-события '{e.path}' (ключ {e.key}) — пустая ссылка.");
                    missing++;
                }

                if (e.isDefault && Enum.TryParse(e.action, out AudioAction action))
                    defaultByAction[action] = reference;
                else if (!e.isDefault)
                    entries.Add(new AudioCatalog.Entry { Key = e.key, Event = reference });
            }

            // Каждое действие AudioAction получает дефолт-слот (пустой, если не замаплен) — иначе
            // валидационный тест «дефолты покрывают все действия» краснеет.
            var defaults = new List<AudioCatalog.ActionDefault>();
            foreach (AudioAction action in Enum.GetValues(typeof(AudioAction)))
                defaults.Add(new AudioCatalog.ActionDefault
                {
                    Action = action,
                    Event = defaultByAction.TryGetValue(action, out EventReference r) ? r : default
                });

            catalog.EditorSetContents(entries.ToArray(), defaults.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AudioCatalog] Наполнен: {entries.Count} записей, {defaults.Count} дефолтов " +
                      $"({resolved} событий резолвнуто, {missing} пропущено).");
        }
    }
}
