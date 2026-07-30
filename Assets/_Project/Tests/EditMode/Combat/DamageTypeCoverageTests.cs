using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Гейт покрытия: ни один источник урона в контенте не оставлен без типа. Инвариант живёт между
    /// файлами — ассет с пустым полем компилируется и запускается, поэтому поймать его может только
    /// тест, который проходит по ВСЕМУ контенту сразу.
    /// </summary>
    /// <remarks>
    /// Заведён реформой 2026-07-30 как замена тому, чего не было: подтип у взрыва костяного щита
    /// остался дефолтным, и это не покраснело ни в тестах, ни в консоли — связка «некромант + крио»
    /// просто молча не работала. Правило общее: если поле обязательно ПО СМЫСЛУ, обязательность
    /// проверяется сканом контента, а не надеждой на внимательность автора.
    /// <para><b>Undefined законен ровно там, где урона нет:</b> у способности без прямого урона
    /// (множитель 0) и у компонента, который урон не наносит. Поэтому тест спрашивает тип не у всех,
    /// а у тех, кто бьёт.</para>
    /// </remarks>
    public sealed class DamageTypeCoverageTests
    {
        /// <summary>
        /// Компоненты эффектов, которые НАНОСЯТ урон и обязаны объявить его тип. Список ручной: у
        /// компонента нет признака «я бью», а вводить его ради теста значило бы менять контракт шва.
        /// Новый бьющий компонент, забытый здесь, останется непокрытым — поэтому список идёт рядом с
        /// проверкой, а не в далёком конфиге.
        /// </summary>
        private static readonly string[] DamagingComponents =
        {
            "PeriodicDamageComponent", "ThornsComponent", "ArmorThornsComponent",
            "IgnitionComponent", "OverloadStrikeComponent", "ShieldBurstComponent",
            "SplitAttackOnTagComponent", "DelayedBurstComponent",
        };

        /// <summary>Фильтрующие компоненты: тип у них — не «чем бью», а «что ловлю», и он тоже обязателен.</summary>
        private static readonly string[] FilteringComponents =
        {
            "SchoolShieldComponent", "SchoolDamageResistComponent",
            "SelfStackOnDealtComponent", "FeedOnDamageComponent",
        };

        [Test]
        public void EveryUnit_DeclaresAutoAttackDamageType()
        {
            var missing = new List<string>();

            foreach (UnitData unit in LoadAll<UnitData>())
                if (unit.AutoAttackDamageType == DamageType.Undefined)
                    missing.Add(AssetDatabase.GetAssetPath(unit));

            Assert.IsEmpty(missing,
                "У этих юнитов не задан тип урона автоатаки — «просто физика» больше не значение:\n"
                + string.Join("\n", missing));
        }

        [Test]
        public void EveryDamagingAbility_DeclaresDamageType()
        {
            var missing = new List<string>();

            foreach (UnitData unit in LoadAll<UnitData>())
            {
                AbilityData[] abilities = unit.Abilities;
                if (abilities == null) continue;

                foreach (AbilityData ability in abilities)
                {
                    if (ability == null || ability.DamageMultiplier <= 0f) continue;   // урона нет — тип не нужен
                    if (ability.DamageType != DamageType.Undefined) continue;
                    missing.Add($"{AssetDatabase.GetAssetPath(unit)} → способность '{ability.Id}' "
                              + $"(множитель {ability.DamageMultiplier})");
                }
            }

            Assert.IsEmpty(missing,
                "Эти способности наносят прямой урон, но тип его не объявлен:\n" + string.Join("\n", missing));
        }

        [Test]
        public void EveryDamagingEffectComponent_DeclaresDamageType()
        {
            var missing = new List<string>();
            string[] interesting = DamagingComponents.Concat(FilteringComponents).ToArray();

            foreach (EffectData effect in LoadAll<EffectData>())
            {
                string path = AssetDatabase.GetAssetPath(effect);

                foreach (object component in Components(effect))
                {
                    if (component == null) continue;
                    string name = component.GetType().Name;
                    if (!interesting.Contains(name)) continue;

                    foreach (FieldInfo field in TypeFields(component.GetType()))
                    {
                        if (field.FieldType != typeof(DamageType)) continue;
                        if ((DamageType)field.GetValue(component) != DamageType.Undefined) continue;

                        // Второй тип двухчастного взрыва законно пуст: доля второй половины нулевая.
                        if (field.Name == "_secondType" && SecondShareIsZero(component)) continue;

                        missing.Add($"{path} → {name}.{field.Name}");
                    }
                }
            }

            Assert.IsEmpty(missing,
                "У этих компонентов эффектов тип урона остался незаданным:\n" + string.Join("\n", missing));
        }

        /// <summary>
        /// Ни один тег конкретики, который выдаёт резолвер, не остался без ассета. Иначе тип урона был
        /// бы в игре, а чип на карточке — нет, и расхождение заметил бы только игрок. Так и вышло с
        /// <see cref="DamageType.Bleed"/>: тип завели, тега не было.
        /// </summary>
        [Test]
        public void EveryDamageType_HasItsTagAsset()
        {
            // Теги ищем сканом ассетов, а не через базу: база наполняется отдельным шагом, и тест про
            // «тег вообще заведён» не должен падать из-за того, что синхронизацию ещё не прогнали.
            var known = new HashSet<string>(LoadAll<TagData>().Select(t => t.Id));

            var missing = new List<string>();
            MethodInfo specific = typeof(UnitTagResolver).GetMethod(
                "SpecificTagId", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(specific, "UnitTagResolver.SpecificTagId переименован — тест ослеп.");

            foreach (DamageType type in DamageTypes.All)
            {
                var id = (string)specific.Invoke(null, new object[] { type });
                if (id == null) continue;                      // у типа нет своего чипа — это законно
                if (!known.Contains(id)) missing.Add($"{type} → {id}");
            }

            Assert.IsEmpty(missing,
                "Резолвер выдаёт теги, которых нет в базе:\n" + string.Join("\n", missing));
        }

        // --- вспомогательное ---

        private static IEnumerable<T> LoadAll<T>() where T : ScriptableObject
            => AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null);

        /// <summary>Компоненты эффекта через рефлексию: поле приватное, а публичного доступа к списку нет.</summary>
        private static IEnumerable<object> Components(EffectData effect)
        {
            FieldInfo field = TypeFields(typeof(EffectData)).FirstOrDefault(f => f.Name == "_components");
            if (field?.GetValue(effect) is not System.Collections.IEnumerable list) return System.Linq.Enumerable.Empty<object>();

            var result = new List<object>();
            foreach (object item in list) result.Add(item);
            return result;
        }

        private static IEnumerable<FieldInfo> TypeFields(System.Type type)
        {
            for (System.Type t = type; t != null; t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    yield return f;
        }

        private static bool SecondShareIsZero(object component)
        {
            FieldInfo share = TypeFields(component.GetType()).FirstOrDefault(f => f.Name == "_secondShare");
            return share != null && (float)share.GetValue(component) <= 0f;
        }
    }
}
