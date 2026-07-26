using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Сериализация <see cref="Vector2"/> как <c>{"x":…,"y":…}</c>.
    /// <para>Без него Newtonsoft разбирает вектор по всем публичным членам, включая свойство
    /// <see cref="Vector2.normalized"/>, у которого есть своё <c>normalized</c> — дерево не заканчивается,
    /// и сохранение позиции сосуда роняет запись. <c>JsonUtility</c> этой беды не знал, потому что читал
    /// только поля; переезд на Newtonsoft её приносит, и закрывать её надо здесь, а не в DTO.</para>
    /// </summary>
    public sealed class Vector2JsonConverter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return existingValue;

            JObject json = JObject.Load(reader);
            return new Vector2(json.Value<float?>("x") ?? 0f, json.Value<float?>("y") ?? 0f);
        }
    }
}
