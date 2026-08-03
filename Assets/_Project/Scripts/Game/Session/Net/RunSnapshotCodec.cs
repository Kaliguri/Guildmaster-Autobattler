using System;
using System.Text;
using Guildmaster.Game.Services;
using Guildmaster.Guild;
using Newtonsoft.Json;
using UnityEngine;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Снимок забега в байты и обратно — тем же DTO и по тем же правилам, какими забег ложится на диск.
    /// </summary>
    /// <remarks>
    /// <b>Почему не свой бинарный формат.</b> <see cref="RunState"/> уже плоский сейв-DTO по строковым
    /// id, и второй формат рядом означал бы второго владельца правил сериализации: добавили поле в
    /// забег — обновили сейв, а снимок молча поехал без него. Отсюда же и общий
    /// <see cref="SaveJson"/>: конвертеры (в первую очередь <c>Vector2</c>) обязаны совпадать.
    /// <para><b>Отступы сняты только здесь.</b> В файле они стоят ради читаемости при разборе багрепорта;
    /// по сети платить за них незачем, а на разбор они не влияют.</para>
    /// <para><b>Цена честно:</b> это несколько килобайт на снимок, и он уезжает целиком на каждое
    /// изменение. Дельта имела бы смысл, будь снимков много в секунду, — но их единицы за узел, и
    /// сложность разошлась бы с выигрышем.</para>
    /// </remarks>
    public static class RunSnapshotCodec
    {
        private static readonly JsonSerializerSettings Settings = CreateSettings();

        /// <summary>Уложить состояние забега в байты. <c>null</c> — сериализовать нечего.</summary>
        public static ArraySegment<byte> Write(RunState state)
        {
            if (state == null) return default;

            string json = JsonConvert.SerializeObject(state, Settings);
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Разобрать снимок. <c>null</c> — снимок не читается этой сборкой; молча подставлять пустой
        /// забег нельзя, гость играл бы в состоянии, которого у хоста нет.
        /// </summary>
        public static RunState Read(ArraySegment<byte> payload)
        {
            if (payload.Count == 0) return null;

            try
            {
                string json = Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count);
                return JsonConvert.DeserializeObject<RunState>(json, Settings);
            }
            catch (JsonException e)
            {
                Debug.LogError($"[RunSnapshotCodec] - снимок забега не разобран: {e.Message}. " +
                               "У хоста и гостя разные версии сборки.");
                return null;
            }
        }

        private static JsonSerializerSettings CreateSettings()
        {
            JsonSerializerSettings settings = SaveJson.CreateSettings();
            settings.Formatting = Formatting.None;
            return settings;
        }
    }
}
