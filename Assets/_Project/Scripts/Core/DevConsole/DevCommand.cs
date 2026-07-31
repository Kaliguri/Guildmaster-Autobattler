using System.Collections.Generic;

namespace Guildmaster.Core.DevConsole
{
    /// <summary>Тип параметра команды: по нему консоль подсказывает форму вызова и разбирает строку.</summary>
    public enum DevParamType
    {
        Int,
        Float,
        Bool,

        /// <summary>Одно слово или строка в кавычках.</summary>
        String,

        /// <summary>Имя значения перечисления; сам тип объявляет команда (<see cref="DevArgs.GetEnum{T}"/>).</summary>
        Enum,
    }

    /// <summary>
    /// Объявленный параметр команды. Нужен ДЛЯ ПОДСКАЗКИ и проверки арности — сам разбор значения делает
    /// <see cref="DevArgs"/> в теле команды, потому что только команда знает, во что превращать слово
    /// (какой именно enum, какие границы).
    /// </summary>
    /// <remarks>
    /// Пара «объявление здесь, чтение там» выглядит избыточной, но альтернатива — типизированные
    /// дескрипторы параметров с рефлексией, как у QFSW: она даёт красивый вызов и платит за это
    /// генериками на каждую арность плюс боксингом аргументов. Для дев-консоли на четыре десятка команд
    /// это не окупается; здесь объявление служит человеку (usage, автодополнение), а не компилятору.
    /// </remarks>
    public sealed class DevParam
    {
        /// <summary>Имя для подсказки: <c>gm_arena_swap &lt;skinId&gt;</c>.</summary>
        public string Name { get; }

        public DevParamType Type { get; }

        /// <summary>Необязательный — команда сработает и без него. Обязательные обязаны идти первыми.</summary>
        public bool Optional { get; }

        public DevParam(string name, DevParamType type, bool optional = false)
        {
            Name     = name;
            Type     = type;
            Optional = optional;
        }

        /// <summary>Как параметр печатается в подсказке: <c>&lt;имя&gt;</c> или <c>[имя]</c>.</summary>
        public override string ToString() => Optional ? $"[{Name}]" : $"<{Name}>";
    }

    /// <summary>
    /// Тело команды. Возвращённая строка печатается консолью как ответ; <c>null</c> — команда молча
    /// сделала дело (её собственные <c>Debug.Log</c> всё равно попадут в вывод — консоль слушает лог).
    /// </summary>
    public delegate string DevCommandHandler(DevArgs args);

    /// <summary>
    /// Одна dev-команда: имя, что делает, форма вызова и тело. Регистрируются модулями через
    /// <see cref="DevCommandRegistry"/>; ни атрибутов, ни рефлексии — модуль сам объявляет свои команды,
    /// поэтому реестр видит ровно то, что зарегистрировано, и работает в тестах без сцены.
    /// </summary>
    public sealed class DevCommand
    {
        private static readonly DevParam[] NoParams = new DevParam[0];

        /// <summary>Имя вызова. Регистр не значим: реестр хранит и ищет в нижнем.</summary>
        public string Name { get; }

        /// <summary>Одна строка «что делает» — печатается в списке команд и в подсказке.</summary>
        public string Summary { get; }

        public IReadOnlyList<DevParam> Params { get; }

        /// <summary>Тело команды.</summary>
        public DevCommandHandler Handler { get; }

        /// <summary>Сколько аргументов обязательно (по объявленным параметрам).</summary>
        public int RequiredCount { get; }

        public DevCommand(string name, string summary, DevCommandHandler handler, params DevParam[] parameters)
        {
            Name    = name;
            Summary = summary;
            Handler = handler;
            Params  = parameters ?? NoParams;

            for (int i = 0; i < Params.Count; i++)
                if (!Params[i].Optional) RequiredCount = i + 1;
        }

        /// <summary>Форма вызова для подсказки: <c>gm_sep_radius &lt;value&gt;</c>.</summary>
        public string Usage
        {
            get
            {
                if (Params.Count == 0) return Name;

                var sb = new System.Text.StringBuilder(Name);
                for (int i = 0; i < Params.Count; i++)
                {
                    sb.Append(' ');
                    sb.Append(Params[i].ToString());
                }
                return sb.ToString();
            }
        }
    }
}
