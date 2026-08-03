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
    /// Общая механика файлового <see cref="ISaveService"/>: конверт со версией схемы, атомарная запись,
    /// бэкап, карантин повреждённого файла, дерево ключей-путей. Отличаются наследники ровно одним —
    /// <b>корневым каталогом</b>, а он решает, поедет ли содержимое в Steam Cloud
    /// (<see cref="JsonFileSaveService"/> — да, <see cref="LocalJsonFileSaveService"/> — нет).
    /// <para>Корень приходит аргументом конструктора наследника, а <b>не дефолтным параметром</b>: у
    /// VContainer дефолтный аргумент примитивного типа означает «ищи регистрацию <c>string</c>» и роняет
    /// всю ветку разрешения зависимостей.</para>
    /// <para>Сериализация — Newtonsoft, не <c>JsonUtility</c>: версию схемы надо прочитать до разбора
    /// payload, миграции (фаза C) — переливка узлов дерева, а <c>JsonUtility</c> вместо отказа молча
    /// разбирает чужой файл в наполовину пустой DTO с валидным видом.</para>
    /// </summary>
    public abstract class JsonFileSaveServiceBase : ISaveService
    {
        private const string FieldSchemaVersion = "schemaVersion";
        private const string FieldGameVersion   = "gameVersion";
        private const string FieldSavedAt       = "savedAtUtc";
        private const string FieldPayload       = "payload";

        private readonly string        _rootFolder;
        private readonly JsonSerializer _serializer;

        protected JsonFileSaveServiceBase(string rootFolder)
        {
            _rootFolder = rootFolder;

            // Правила сериализации живут в SaveJson, а не здесь: тем же DTO и по тем же правилам забег
            // уезжает гостю по сети, и вторая копия настроек разошлась бы с этой молча.
            _serializer = SaveJson.CreateSerializer();
        }

        // Корень — GameDataPath, а НЕ persistentDataPath: путь к данным игрока не должен зависеть от
        // маркетингового имени игры, иначе переименование после релиза уводит игру на пустой каталог.
        private string Root => Path.Combine(GameDataPath.Root, _rootFolder);

        private string PathFor(string key) =>
            Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");

        private string TempFor(string key)   => PathFor(key) + ".tmp";
        private string BackupFor(string key) => PathFor(key) + ".bak";

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
                Debug.LogError($"[{GetType().Name}] - не удалось сохранить '{key}': {e.Message}");
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
                Debug.LogWarning($"[{GetType().Name}] - '{key}' был повреждён, восстановлен из .bak");
                File.Copy(backup, path, overwrite: true);
                return Interpret<T>(fromBackup, key);
            }

            Debug.LogError($"[{GetType().Name}] - '{key}' повреждён и бэкапа нет; файл отложен как .corrupt");
            return SaveLoadResult<T>.Corrupted();
        }

        public bool Exists(string key) => File.Exists(PathFor(key));

        public void Delete(string key)
        {
            TryDelete(PathFor(key));
            TryDelete(BackupFor(key));
        }

        public void DeleteTree(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return; // защита от сноса всего корня опечаткой

            string directory = Path.Combine(Root, prefix.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[{GetType().Name}] - не удалось удалить поддерево '{prefix}': {e.Message}");
            }
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
                Debug.LogError($"[{GetType().Name}] - не удалось перечислить '{prefix}': {e.Message}");
            }

            return keys;
        }

        /// <summary>
        /// Решение по версии схемы — три исхода (ТЗ §5). Молчаливая загрузка чужой версии здесь и была бы
        /// той самой тихой потерей прогресса: разобрали бы наполовину, а следующий автосейв затёр.
        /// </summary>
        private SaveLoadResult<T> Interpret<T>(JObject envelope, string key)
        {
            int    savedVersion = envelope.Value<int?>(FieldSchemaVersion) ?? 0;
            string gameVersion  = envelope.Value<string>(FieldGameVersion) ?? string.Empty;
            JToken payload      = envelope[FieldPayload];

            if (savedVersion <= 0 || payload == null)
            {
                Debug.LogError($"[{GetType().Name}] - '{key}': нет конверта (schemaVersion/payload), файл не наш");
                return SaveLoadResult<T>.Corrupted();
            }

            int currentVersion = SaveSchema.VersionOf<T>();

            if (savedVersion > currentVersion)
            {
                Debug.LogWarning($"[{GetType().Name}] - '{key}' записан более новой версией игры " +
                                 $"({gameVersion}, схема {savedVersion} > {currentVersion}): не гружу и не трогаю");
                return SaveLoadResult<T>.TooNew(savedVersion, gameVersion);
            }

            if (savedVersion < currentVersion)
            {
                // Миграции — фаза C ТЗ. До неё поднять старую схему нечем, и честный отказ лучше
                // молчаливого разбора «как получится».
                Debug.LogWarning($"[{GetType().Name}] - '{key}' старой схемы {savedVersion} " +
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
                Debug.LogError($"[{GetType().Name}] - '{key}': не удалось разобрать payload: {e.Message}");
                return SaveLoadResult<T>.Corrupted();
            }
        }

        private bool TryReadEnvelope(string path, out JObject envelope)
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
                Debug.LogError($"[{GetType().Name}] - не удалось прочитать '{path}': {e.Message}");
                return false;
            }
        }

        /// <summary>Отложить нечитаемый файл, чтобы <see cref="Exists"/> перестал считать его сейвом.</summary>
        private void Quarantine(string path)
        {
            try
            {
                string target = path + ".corrupt";
                if (File.Exists(target)) File.Delete(target); // держим только последний — диск не копим
                File.Move(path, target);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[{GetType().Name}] - не удалось отложить повреждённый '{path}': {e.Message}");
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[{GetType().Name}] - не удалось удалить '{path}': {e.Message}");
            }
        }
    }
}
