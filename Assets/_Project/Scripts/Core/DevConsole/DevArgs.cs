using System;
using System.Collections.Generic;
using System.Globalization;

namespace Guildmaster.Core.DevConsole
{
    /// <summary>
    /// Аргумент не разобрался или его нет. Ловит <see cref="DevCommandRegistry.Execute"/> и печатает
    /// сообщение вместе с формой вызова — команде разбираться с этим не нужно.
    /// </summary>
    public sealed class DevArgException : Exception
    {
        public DevArgException(string message) : base(message) { }
    }

    /// <summary>
    /// Аргументы вызова: слова строки после имени команды. Читаются по индексу с проверкой —
    /// <c>args.GetFloat(0)</c> либо вернёт число, либо бросит <see cref="DevArgException"/> с внятным
    /// текстом.
    /// </summary>
    /// <remarks>
    /// <b>Числа разбираются в инвариантной культуре, и это не формальность.</b> Локаль машины у нас
    /// русская, где десятичный разделитель — запятая: <c>float.Parse("0.35")</c> без явной культуры на
    /// ней провалится, и команда <c>gm_sep_ally 0.35</c> начала бы огрызаться на правильный ввод.
    /// Консоль — инструмент разработчика, её синтаксис не зависит от языка системы.
    /// </remarks>
    public sealed class DevArgs
    {
        private readonly IReadOnlyList<string> _tokens;
        private readonly int _offset;

        /// <param name="tokens">Токены строки целиком, включая имя команды.</param>
        /// <param name="offset">С какого токена начинаются аргументы (обычно 1 — после имени).</param>
        public DevArgs(IReadOnlyList<string> tokens, int offset = 1)
        {
            _tokens = tokens;
            _offset = offset;
        }

        /// <summary>Сколько аргументов передано (имя команды не считается).</summary>
        public int Count => _tokens == null ? 0 : Math.Max(0, _tokens.Count - _offset);

        /// <summary>Передан ли аргумент с этим индексом.</summary>
        public bool Has(int index) => index >= 0 && index < Count;

        /// <summary>Сырое слово или <c>null</c>, если его нет. Не бросает — для необязательных аргументов.</summary>
        public string Raw(int index) => Has(index) ? _tokens[_offset + index] : null;

        /// <summary>Строка. Обязательный аргумент: нет — исключение.</summary>
        public string GetString(int index)
        {
            string raw = Raw(index);
            if (raw == null) throw Missing(index, "строку");
            return raw;
        }

        /// <summary>Строка или <paramref name="fallback"/>, если аргумент не передан.</summary>
        public string GetString(int index, string fallback) => Raw(index) ?? fallback;

        public int GetInt(int index)
        {
            string raw = Raw(index);
            if (raw == null) throw Missing(index, "целое число");
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw Bad(index, raw, "целое число");
            return value;
        }

        public int GetInt(int index, int fallback) => Has(index) ? GetInt(index) : fallback;

        public float GetFloat(int index)
        {
            string raw = Raw(index);
            if (raw == null) throw Missing(index, "число");
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw Bad(index, raw, "число");
            return value;
        }

        public float GetFloat(int index, float fallback) => Has(index) ? GetFloat(index) : fallback;

        /// <summary>
        /// Булево. Принимает <c>true/false</c>, <c>1/0</c>, <c>on/off</c>, <c>yes/no</c> — набор
        /// намеренно широкий: тумблеры дёргают на бегу, и спорить с человеком о слове «on» инструменту не к лицу.
        /// </summary>
        public bool GetBool(int index)
        {
            string raw = Raw(index);
            if (raw == null) throw Missing(index, "true или false");

            switch (raw.ToLowerInvariant())
            {
                case "true":  case "1": case "on":  case "yes": return true;
                case "false": case "0": case "off": case "no":  return false;
                default: throw Bad(index, raw, "true или false");
            }
        }

        public bool GetBool(int index, bool fallback) => Has(index) ? GetBool(index) : fallback;

        /// <summary>Значение перечисления по имени, регистр не значим. В сообщении об ошибке — список допустимых.</summary>
        public T GetEnum<T>(int index) where T : struct, Enum
        {
            string raw = Raw(index);
            if (raw == null) throw Missing(index, $"одно из: {string.Join(", ", Enum.GetNames(typeof(T)))}");

            if (!Enum.TryParse(raw, ignoreCase: true, out T value))
                throw Bad(index, raw, $"одно из: {string.Join(", ", Enum.GetNames(typeof(T)))}");

            return value;
        }

        public T GetEnum<T>(int index, T fallback) where T : struct, Enum =>
            Has(index) ? GetEnum<T>(index) : fallback;

        private static DevArgException Missing(int index, string expected) =>
            new DevArgException($"не хватает аргумента #{index + 1}: ожидалось {expected}");

        private static DevArgException Bad(int index, string raw, string expected) =>
            new DevArgException($"аргумент #{index + 1} «{raw}» — ожидалось {expected}");
    }

    /// <summary>Разбор строки консоли на слова.</summary>
    public static class DevCommandLine
    {
        /// <summary>
        /// Разбить строку на токены: разделитель — пробелы, кавычки склеивают слова в один токен
        /// (<c>gm_say "два слова"</c> = два токена). Возвращает число токенов, дописанных в
        /// <paramref name="result"/>.
        /// </summary>
        /// <remarks>
        /// Незакрытая кавычка НЕ считается ошибкой: строка добирается до конца ввода. Человек печатает
        /// команду слева направо, и ругаться на незакрытую кавычку в момент, когда он ещё не дописал, —
        /// худшее, что может сделать подсказка.
        /// </remarks>
        public static int Tokenize(string line, List<string> result)
        {
            if (result == null) return 0;
            result.Clear();
            if (string.IsNullOrWhiteSpace(line)) return 0;

            int i = 0;
            var token = new System.Text.StringBuilder();

            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                token.Clear();

                if (line[i] == '"')
                {
                    i++;
                    while (i < line.Length && line[i] != '"') token.Append(line[i++]);
                    if (i < line.Length) i++; // закрывающая кавычка
                }
                else
                {
                    while (i < line.Length && !char.IsWhiteSpace(line[i])) token.Append(line[i++]);
                }

                result.Add(token.ToString());
            }

            return result.Count;
        }
    }
}
