using System;
using System.IO;
using Guildmaster.Core.Persistence;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Соло-бэкенд <see cref="ISaveService"/>: JSON-файл на диск (<see cref="Application.persistentDataPath"/>)
    /// через <see cref="JsonUtility"/>. Без зависимости от Easy Save 3 (у ES3 нет asmdef → из
    /// <c>Guildmaster.*</c> не вызвать). ES3 + Steam Cloud — плановая замена бэкенда за тем же интерфейсом
    /// (вики «2»), тела не трогая. JsonUtility сериализует <c>[Serializable]</c>-DTO с публичными полями.
    /// <para>Запись атомарна: сначала во временный файл, затем подмена целевого с откладыванием прежнего
    /// в <c>.bak</c>. Прерывание записи (краш, выключение) больше не может обрезать забег — до подмены
    /// целевой файл не тронут, после подмены он целиком новый (аудит 2026-07-26, C-02).</para>
    /// <para>Повреждённый файл не выдаётся за отсутствующий: он уезжает в <c>.corrupt</c>, а бэкап, если он
    /// есть, пробуется как замена. Иначе <see cref="Exists"/> продолжал бы отвечать «сейв есть», кнопка
    /// «Продолжить» оставалась бы на экране и молча ничего не делала (C-01).</para>
    /// <para>ОГРАНИЧЕНИЕ: <see cref="JsonUtility"/> не жалуется на JSON, где просто нет нужных полей — такой
    /// файл разберётся в DTO с дефолтами. Ловится только нечитаемый JSON и пустой результат; версионирование
    /// схемы — отдельная работа (<c>RunState.SchemaVersion</c> сейчас пишется, но не читается).</para>
    /// </summary>
    public sealed class JsonFileSaveService : ISaveService
    {
        private static string PathFor(string key) =>
            Path.Combine(Application.persistentDataPath, key + ".json");

        private static string TempFor(string key)   => PathFor(key) + ".tmp";
        private static string BackupFor(string key) => PathFor(key) + ".bak";

        public void Save<T>(string key, T value)
        {
            string path = PathFor(key);
            string temp = TempFor(key);

            try
            {
                // Полностью пишем рядом и только потом подменяем: целевой файл либо старый целиком,
                // либо новый целиком, третьего состояния на диске не возникает.
                File.WriteAllText(temp, JsonUtility.ToJson(value, prettyPrint: true));

                if (File.Exists(path)) File.Replace(temp, path, BackupFor(key), ignoreMetadataErrors: true);
                else                   File.Move(temp, path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось сохранить '{key}': {e.Message}");
                TryDelete(temp);
            }
        }

        public T Load<T>(string key)
        {
            string path = PathFor(key);
            if (!File.Exists(path)) return default;

            if (TryRead(path, out T value)) return value;

            // Файл есть, но не читается. Откладываем его (данные игрока не удаляем) и пробуем бэкап —
            // он остался от предыдущей успешной записи.
            Quarantine(path);

            string backup = BackupFor(key);
            if (File.Exists(backup) && TryRead(backup, out T fromBackup))
            {
                Debug.LogWarning($"[JsonFileSaveService] - '{key}' был повреждён, восстановлен из .bak");
                File.Copy(backup, path, overwrite: true);
                return fromBackup;
            }

            Debug.LogError($"[JsonFileSaveService] - '{key}' повреждён и бэкапа нет; файл отложен как .corrupt");
            return default;
        }

        public bool Exists(string key) => File.Exists(PathFor(key));

        public void Delete(string key)
        {
            TryDelete(PathFor(key));
            TryDelete(BackupFor(key));
        }

        private static bool TryRead<T>(string path, out T value)
        {
            value = default;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;

                value = JsonUtility.FromJson<T>(json);
                return value != null;
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
