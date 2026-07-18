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
    /// </summary>
    public sealed class JsonFileSaveService : ISaveService
    {
        private static string PathFor(string key) =>
            Path.Combine(Application.persistentDataPath, key + ".json");

        public void Save<T>(string key, T value)
        {
            try
            {
                File.WriteAllText(PathFor(key), JsonUtility.ToJson(value, prettyPrint: true));
            }
            catch (IOException e)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось сохранить '{key}': {e.Message}");
            }
        }

        public T Load<T>(string key)
        {
            string path = PathFor(key);
            if (!File.Exists(path)) return default;
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[JsonFileSaveService] - не удалось загрузить '{key}': {e.Message}");
                return default;
            }
        }

        public bool Exists(string key) => File.Exists(PathFor(key));

        public void Delete(string key)
        {
            string path = PathFor(key);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
