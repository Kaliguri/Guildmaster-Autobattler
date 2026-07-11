using System;
using System.Collections.Generic;
using System.Text;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Маппинг тип контента → префикс домена id и генерация id вида <c>domain.lower_snake</c> из имени
    /// ассета (вики «13» §2.2). Пополняется по мере добавления контент-типов; концертные типы, чтобы
    /// наследники (напр. RelicData/EnemyData от UnitData в Фазе 4) получали разные домены.
    /// </summary>
    public static class ContentDomains
    {
        // Концертный тип → домен. Наследники резолвятся по ближайшему зарегистрированному предку (fallback).
        private static readonly Dictionary<Type, string> Domains = new Dictionary<Type, string>
        {
            { typeof(RelicData),       "relic"       },
            { typeof(EnemyData),       "enemy"       },
            { typeof(VesselData),      "vessel"      },
            { typeof(EffectData),      "effect"      },
            { typeof(TagData),         "tag"         },
            { typeof(TraitData),       "trait"       },
            { typeof(ConsequenceData), "consequence" },
            { typeof(AIPresetData),    "ai_preset"   },
            { typeof(GuildmasterData), "guildmaster" },
            { typeof(ItemData),        "item"        },
            { typeof(RunModifierData), "run_mod"     },
        };

        /// <summary>Найти домен для типа (точное совпадение или ближайший зарегистрированный предок).</summary>
        public static bool TryGetDomain(Type type, out string domain)
        {
            for (Type cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                if (Domains.TryGetValue(cur, out domain)) return true;
            }
            domain = null;
            return false;
        }

        /// <summary>Домен типа или исключение (отсутствие маппинга = ошибка конфигурации, не тихий дефолт).</summary>
        public static string GetDomain(Type type) =>
            TryGetDomain(type, out string domain)
                ? domain
                : throw new ArgumentException(
                    $"No content domain registered for type '{type.Name}'. Add it to {nameof(ContentDomains)}.");

        /// <summary>
        /// Собрать id <c>domain.lower_snake</c> из имени ассета: <c>("IceChainsStun")</c> → <c>effect.ice_chains_stun</c>.
        /// </summary>
        public static string MakeId(Type type, string assetName) =>
            $"{GetDomain(type)}.{ToSnakeCase(assetName)}";

        /// <summary>PascalCase / пробелы / прочие разделители → <c>lower_snake</c> (только [a-z0-9_]).</summary>
        public static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length + 8);
            bool prevWasLowerOrDigit = false;

            foreach (char c in name)
            {
                if (char.IsUpper(c))
                {
                    // Граница слова перед заглавной, если предыдущий был строчной/цифрой (Pascal → snake).
                    if (prevWasLowerOrDigit && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                    prevWasLowerOrDigit = false;
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                    prevWasLowerOrDigit = true;
                }
                else
                {
                    // Любой разделитель (пробел/дефис/…) → одиночное подчёркивание.
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    prevWasLowerOrDigit = false;
                }
            }

            // Убираем хвостовое подчёркивание, если осталось.
            if (sb.Length > 0 && sb[sb.Length - 1] == '_') sb.Length--;
            return sb.ToString();
        }
    }
}
