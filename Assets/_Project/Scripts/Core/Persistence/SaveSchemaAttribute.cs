using System;
using System.Collections.Generic;
using System.Reflection;

namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Версия схемы сохраняемого DTO (ТЗ [[save-system]] §5). Вешается на сам тип, а не хранится полем
    /// внутри состояния: версия — свойство ФАЙЛА, а не забега, и живёт в конверте рядом с полезной
    /// нагрузкой. Один владелец факта — этот атрибут.
    /// <para>Бампать при <b>ломающем</b> изменении: переименовали поле, сменили тип, сменили смысл или
    /// единицы измерения. Добавление поля с безопасным дефолтом и удаление поля бампа НЕ требуют —
    /// старый файл разберётся, лишнее в JSON игнорируется.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SaveSchemaAttribute : Attribute
    {
        public int Version { get; }

        public SaveSchemaAttribute(int version) => Version = version;
    }

    /// <summary>Резолв версии схемы по типу DTO. Рефлексия кешируется — вызывается на каждом сейве.</summary>
    public static class SaveSchema
    {
        private const int Unversioned = 1;

        private static readonly Dictionary<Type, int> Cache = new();

        /// <summary>
        /// Текущая версия схемы для типа. Тип без атрибута считается версией 1 — это не молчаливая
        /// подстановка, а осознанный старт отсчёта: пока никто не бампал, версия и есть первая.
        /// </summary>
        public static int VersionOf(Type type)
        {
            if (Cache.TryGetValue(type, out int cached)) return cached;

            var attribute = type.GetCustomAttribute<SaveSchemaAttribute>(inherit: false);
            int version = attribute?.Version ?? Unversioned;
            Cache[type] = version;
            return version;
        }

        /// <inheritdoc cref="VersionOf(Type)"/>
        public static int VersionOf<T>() => VersionOf(typeof(T));
    }
}
