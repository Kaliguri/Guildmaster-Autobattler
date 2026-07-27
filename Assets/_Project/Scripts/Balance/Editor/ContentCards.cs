using System.Collections.Generic;
using System.Text;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Карточки контента для сайта отчётов: как кита ЗОВУТ и что он умеет — русское имя, описание,
    /// роль, теги и разбор способностей. Не замер: справка, без которой таблица чисел безымянна.
    /// </summary>
    /// <remarks>
    /// Имя ассета (<c>Cryomancer</c>) остаётся техническим ключом — по нему сшиваются режимы между
    /// собой и с прошлыми прогонами. Показывать его игроку-дизайнеру незачем: он думает о «Криоманте».
    /// Поэтому снимок даёт обе стороны, а сшивка не ломается.
    /// <para>Тексты берутся из ЖИВОЙ таблицы локализации (<c>Content_ru</c>), а не дублируются здесь:
    /// один владелец у строки — таблица. Разметка ключевых слов разворачивается тем же
    /// <see cref="KeywordMarkup"/>, что и в игре, иначе сайт показывал бы сырое <c>[kw:frozen:acc]</c>.</para>
    /// </remarks>
    public static class ContentCards
    {
        private const string RuTablePath = "Assets/_Project/Localization/Tables/Content_ru.asset";

        /// <summary>Кит-точка-отсчёта: классовая норма без механик. Из выводов о балансе исключается.</summary>
        private const string ReferenceUnitId = "relic.base";

        public static (string csv, string md) Run()
        {
            StringTable ru = AssetDatabase.LoadAssetAtPath<StringTable>(RuTablePath);
            if (ru == null)
            {
                Debug.LogError($"[SimBench] Таблица локализации не найдена: {RuTablePath}. " +
                               "Карточки соберутся, но без русских имён.");
            }

            var units = new List<UnitData>();
            units.AddRange(BalanceAssets.LoadRelics());
            units.AddRange(BalanceAssets.LoadEnemies());

            (string csv, string md) cards = WriteCards(ru, units);
            WriteAbilities(ru, units);
            return cards;
        }

        // ---------------------------------------------------------------- карточки китов

        private static (string csv, string md) WriteCards(StringTable ru, List<UnitData> units)
        {
            var headers = new List<string> { "Relic", "Id", "Name", "Desc", "Class", "Kind", "Tags", "AbilityIds" };
            var table = new List<IReadOnlyList<object>>();

            foreach (UnitData unit in units)
            {
                table.Add(new object[]
                {
                    unit.name,
                    unit.Id,
                    Text(ru, unit.Id + ".name"),
                    Text(ru, unit.Id + ".desc"),
                    unit.CombatClass.ToString(),
                    Kind(unit),
                    Tags(ru, unit),
                    AbilityIds(unit),
                });
            }

            const string notes =
                "Справочные карточки: как кита зовут по-русски, что он собой представляет, какая у него " +
                "роль и теги. Колонка Relic — техническое имя ассета, по нему сшиваются все прочие отчёты.";

            string csv = ReportWriter.WriteCsv("content_cards", headers, table);
            string md = ReportWriter.WriteMarkdown("content_cards", "Карточки контента", headers, table, notes);
            ReportWriter.WriteJson("content_cards", "Карточки контента", headers, table, notes);
            return (csv, md);
        }

        // ---------------------------------------------------------------- способности

        /// <summary>
        /// Отдельный снимок: строка = ОДНА способность. Способности у китов разной длины, и загонять их
        /// в одну ячейку значило бы городить свой формат внутри формата — сайт группирует строки сам.
        /// </summary>
        private static void WriteAbilities(StringTable ru, List<UnitData> units)
        {
            var headers = new List<string>
            {
                "Relic", "Ability", "Cooldown", "Cost", "DmgMult", "Target", "Radius", "Heal", "Effects", "EffectDesc",
            };
            var table = new List<IReadOnlyList<object>>();

            foreach (UnitData unit in units)
            {
                AbilityData[] abilities = unit.Abilities;
                if (abilities == null) continue;

                foreach (AbilityData ability in abilities)
                {
                    if (ability == null) continue;

                    float heal = ability.HealFlat > 0f ? ability.HealFlat
                               : ability.HealPctTargetMissingHp > 0f ? ability.HealPctTargetMissingHp * 100f
                               : 0f;

                    table.Add(new object[]
                    {
                        unit.name,
                        ability.Id,
                        ability.BaseCooldown,
                        ability.ResourceCost,
                        ability.DamageMultiplier,
                        ability.TargetMode.ToString(),
                        ability.AreaRadius,
                        heal,
                        EffectNames(ru, ability),
                        EffectDescriptions(ru, ability),
                    });
                }
            }

            const string notes =
                "Разбор способностей: по строке на способность. Cooldown — базовый кулдаун в секундах, " +
                "Cost — стоимость ресурса, DmgMult — множитель урона от авто-атаки, Radius — радиус области " +
                "(0 = одиночная цель), Heal — плоское лечение или процент недостающего HP. " +
                "Effects — что способность накладывает, с описаниями из той же таблицы, что видит игрок.";

            ReportWriter.WriteCsv("content_abilities", headers, table);
            ReportWriter.WriteMarkdown("content_abilities", "Способности китов", headers, table, notes);
            ReportWriter.WriteJson("content_abilities", "Способности китов", headers, table, notes);
        }

        // ---------------------------------------------------------------- тексты

        /// <summary>
        /// Строка из русской таблицы с развёрнутой разметкой терминов. Ключа нет — пусто: дырку в
        /// локализации сайт покажет пустой ячейкой, а не выдумает подпись.
        /// </summary>
        private static string Text(StringTable ru, string key)
        {
            string raw = Raw(ru, key);
            return string.IsNullOrEmpty(raw) ? string.Empty : KeywordMarkup.Strip(raw, (id, form) => Word(ru, id, form));
        }

        private static string Raw(StringTable ru, string key)
        {
            if (ru == null || string.IsNullOrEmpty(key)) return string.Empty;
            StringTableEntry entry = ru.GetEntry(key);
            return entry != null ? entry.Value : string.Empty;
        }

        /// <summary>Форма термина: сначала падежный ключ, затем именительный — как в игре.</summary>
        private static string Word(StringTable ru, string id, string caseTag)
        {
            if (!string.IsNullOrEmpty(caseTag))
            {
                string cased = Raw(ru, id + ".name." + caseTag);
                if (!string.IsNullOrEmpty(cased)) return cased;
            }
            return Raw(ru, id + ".name");
        }

        /// <summary>
        /// Сторона контента — и отдельно ЭТАЛОН. «Пустой сосуд» это Брузер по классовой норме без
        /// единого эффекта: точка отсчёта, а не участник баланса. Стенд обязан отличать его от
        /// обычного кита, иначе он вечно висит в аутсайдерах, хотя проблемой быть не может по замыслу.
        /// </summary>
        private static string Kind(UnitData unit)
        {
            if (unit.Id == ReferenceUnitId) return "Эталон";
            return unit is RelicData ? "Реликвия" : "Враг";
        }

        private static string Tags(StringTable ru, UnitData unit)
        {
            TagData[] tags = unit.InfoTags;
            if (tags == null || tags.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (TagData tag in tags)
            {
                if (tag == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                string name = Raw(ru, tag.Id + ".name");
                sb.Append(string.IsNullOrEmpty(name) ? tag.name : name);
            }
            return sb.ToString();
        }

        private static string AbilityIds(UnitData unit)
        {
            AbilityData[] abilities = unit.Abilities;
            if (abilities == null || abilities.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (AbilityData ability in abilities)
            {
                if (ability == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(ability.Id);
            }
            return sb.ToString();
        }

        private static string EffectNames(StringTable ru, AbilityData ability)
        {
            EffectData[] effects = ability.Effects;
            if (effects == null || effects.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (EffectData effect in effects)
            {
                if (effect == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                string name = Raw(ru, effect.Id + ".name");
                sb.Append(string.IsNullOrEmpty(name) ? effect.name : name);
            }
            return sb.ToString();
        }

        private static string EffectDescriptions(StringTable ru, AbilityData ability)
        {
            EffectData[] effects = ability.Effects;
            if (effects == null || effects.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (EffectData effect in effects)
            {
                if (effect == null) continue;
                string desc = Text(ru, effect.Id + ".desc");
                if (string.IsNullOrEmpty(desc)) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(desc);
            }
            return sb.ToString();
        }
    }
}
