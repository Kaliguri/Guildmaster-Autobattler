using System;
using System.Collections.Generic;
using System.IO;
using Guildmaster.Core.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Бэкенд <see cref="ISaveService"/> — наш собственный и единственный: JSON-файл на диск под
    /// <c>persistentDataPath/Saves</c> (ТЗ [[save-system]] §4).
    /// <para>Easy Save 3 остаётся в проекте РЕФЕРЕНСОМ, а не плановой заменой (реш. 2026-07-26): мы
    /// сохраняем данные, а не объекты — durable-состояние это плоский DTO по строковым id, — поэтому
    /// сильные стороны ES3 (графы объектов, ссылки на UnityEngine.Object, полиморфизм) решают проблему,
    /// которой у нас нет.</para>
    /// <para><b>Каталог <c>Saves/</c> — контракт со Steam Cloud:</b> Auto-Cloud синхронизирует его по маске
    /// <c>*.json</c> рекурсивно. Поэтому суффиксы служебных файлов идут ПОСЛЕ расширения
    /// (<c>run.json.bak</c>, не <c>run.bak.json</c>) — так они не подпадают под маску и мусор не едет в
    /// облако. Местами не менять.</para>
    /// <para>Запись атомарна: сначала во временный файл, затем подмена целевого с откладыванием прежнего
    /// в <c>.bak</c>. Прерывание записи (краш, выключение) не может обрезать забег — до подмены целевой
    /// файл не тронут, после подмены он целиком новый.</para>
    /// <para>Повреждённый файл не выдаётся за отсутствующий: он уезжает в <c>.corrupt</c>, а бэкап, если он
    /// есть, пробуется как замена.</para>
    /// <para>Сериализация — Newtonsoft (не <c>JsonUtility</c>): нужен доступ к дереву JSON, иначе версию
    /// схемы не прочитать раньше разбора, а миграции (фаза C) пришлось бы делать классами-двойниками
    /// <c>RunStateV1/V2/V3</c>. Заодно уходит главная ловушка <c>JsonUtility</c> — он молча разбирает
    /// чужой файл в наполовину пустой DTO с валидным видом.</para>
    /// </summary>
    public sealed class JsonFileSaveService : ISaveService
    {
        /// <summary>Корень синхронизируемых сохранений. Всё, что НЕ едет в облако, живёт вне его.</summary>
        public const string SavesFolder = "Saves";

        private const string FieldSchemaVersion = "schemaVersion";
        private const string FieldGameVersion   = "gameVersion";
        private const string FieldSavedAt       = "savedAtUtc";
        private const string FieldPayload       = "payload";

        private readonly JsonSerializer _serializer;

        public JsonFileSaveService()
        {
            var settings = new JsonSerializerSettings
            {
                // Тип с новым полем должен читать старый файл: отсутствующее поле остаётся дефолтом
                // конструктора DTO, а не роняет загрузку. Бампа схемы такое изменение не требует (§5).
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting            = Formatting.Indented,
            };
            settings.Converters.Add(new Vector2JsonConverter());

            _serializer = JsonSerializer.Create(settings);
        }

        private static string Root => Path.Combine(Application.persistentDataPath, SavesFolder);

        private static string PathFor(string key) => Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");

        private static string TempFor(string key)   => PathFor(key) + ".tmp";
        private static string BackupFor(string key) => PathFor(key) + ".bak";

        public void Save<T>(string key, T value)
        {
            string path = PathFor(key);
            string temp = TempFor(key);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);

                var envelope = new JObject
                {
                    // Версия схемы — свойство файла, а не состояния: она живёт здесь, а не полем в DTO.
                    [FieldSchemaVersion] = SaveSchema.VersionOf<T>(),
                    // Обе строки ниже — исключительно для багрепортов; решения по ним не принимаются (§5).
                    [FieldGameVersion]   = Application.version,
                    [FieldSavedAt]       = DateTime.UtcNow.ToString("o"),
                    [FieldPayload]       = JToken.FromObject(value, _serializer),
                };

                // Полностью пишем рядом и только потом подменяем: целевой файл либо старый целиком,
                // либо новый целиком, третьего состояния на диске не возникает.
                File.WriteAllText(temp, envelope.ToString(Formatting.Indented));

                if (File.Exists(path)) File.Replace(temp, path, BackupFor(key), ignoreMetadataErrors: true);
                else                   File.Move(temp, path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось сохранить '{key}': {e.Message}");
                TryDelete(temp);
            }
        }

        public SaveLoadResult<T> TryLoad<T>(string key)
        {
            string path = PathFor(key);
            if (!File.Exists(path)) return SaveLoadResult<T>.Missing();

            if (TryReadEnvelope(path, out JObject envelope))
                return Interpret<T>(envelope, key);

            // Файл есть, но не читается. Откладываем его (данные игрока не удаляем) и пробуем бэкап —
            // он остался от предыдущей успешной записи.
            Quarantine(path);

            string backup = BackupFor(key);
            if (File.Exists(backup) && TryReadEnvelope(backup, out JObject fromBackup))
            {
                Debug.LogWarning($"[JsonFileSaveService] - '{key}' был повреждён, восстановлен из .bak");
                File.Copy(backup, path, overwrite: true);
                return Interpret<T>(fromBackup, key);
            }

            Debug.LogError($"[JsonFileSaveService] - '{key}' повреждён и бэкапа нет; файл отложен как .corrupt");
            return SaveLoadResult<T>.Corrupted();
        }

        public bool Exists(string key) => File.Exists(PathFor(key));

        public void Delete(string key)
        {
            TryDelete(PathFor(key));
            TryDelete(BackupFor(key));
        }

        public IReadOnlyList<string> List(string prefix)
        {
            string directory = string.IsNullOrEmpty(prefix)
                ? Root
                : Path.Combine(Root, prefix.Replace('/', Path.DirectorySeparatorChar));

            var keys = new List<string>();
            if (!Directory.Exists(directory)) return keys;

            try
            {
                foreach (string path in Directory.GetDirectories(directory))
                    keys.Add(Path.GetFileName(path));

                // Файлы отдаём без расширения — вызывающий мыслит ключами, а не путями. Служебные
                // .bak/.tmp/.corrupt отсеиваются сами: у них расширение не .json.
                foreach (string path in Directory.GetFiles(directory, "*.json"))
                    keys.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось перечислить '{prefix}': {e.Message}");
            }

            return keys;
        }

        /// <summary>
        /// Решение по версии схемы — три исхода (§5). Молчаливая загрузка чужой версии здесь и была бы
        /// той самой тихой потерей прогресса: разобрали бы наполовину, а следующий автосейв затёр.
        /// </summary>
        private SaveLoadResult<T> Interpret<T>(JObject envelope, string key)
        {
            int    savedVersion = envelope.Value<int?>(FieldSchemaVersion) ?? 0;
            string gameVersion  = envelope.Value<string>(FieldGameVersion) ?? string.Empty;
            JToken payload      = envelope[FieldPayload];

            if (savedVersion <= 0 || payload == null)
            {
                Debug.LogError($"[JsonFileSaveService] - '{key}': нет конверта (schemaVersion/payload), файл не наш");
                return SaveLoadResult<T>.Corrupted();
            }

            int currentVersion = SaveSchema.VersionOf<T>();

            if (savedVersion > currentVersion)
            {
                Debug.LogWarning($"[JsonFileSaveService] - '{key}' записан более новой версией игры " +
                                 $"({gameVersion}, схема {savedVersion} > {currentVersion}): не гружу и не трогаю");
                return SaveLoadResult<T>.TooNew(savedVersion, gameVersion);
            }

            if (savedVersion < currentVersion)
            {
                // Миграции — фаза C ТЗ. До неё поднять старую схему нечем, и честный отказ лучше
                // молчаливого разбора «как получится».
                Debug.LogWarning($"[JsonFileSaveService] - '{key}' старой схемы {savedVersion} " +
                                 $"(текущая {currentVersion}), миграции ещё не реализованы");
                return SaveLoadResult<T>.Unsupported(savedVersion, gameVersion);
            }

            try
            {
                var value = payload.ToObject<T>(_serializer);
                if (value == null) return SaveLoadResult<T>.Corrupted();

                return SaveLoadResult<T>.Ok(value, savedVersion, gameVersion);
            }
            catch (JsonException e)
            {
                Debug.LogError($"[JsonFileSaveService] - '{key}': не удалось разобрать payload: {e.Message}");
                return SaveLoadResult<T>.Corrupted();
            }
        }

        private static bool TryReadEnvelope(string path, out JObject envelope)
        {
            envelope = null;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;

                envelope = JObject.Parse(json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось прочитать '{path}': {e.Message}");
                return false;
            }
        }

        /// <summary>Отложить нечитаемый файл, чтобы <see cref="Exists"/> перестал считать его сейвом.</summary>
        private static void Quarantine(string path)
        {
            try
            {
                string target = path + ".corrupt";
                if (File.Exists(target)) File.Delete(target); // держим только последний — диск не копим
                File.Move(path, target);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось отложить повреждённый '{path}': {e.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось удалить '{path}': {e.Message}");
            }
        }
    }
}
