using Guildmaster.Balance.Editor;
using Guildmaster.Combat;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Balance.Tests
{
    /// <summary>
    /// Инвариант шва «норма класса → стенд»: единственный владелец классовых норм —
    /// <see cref="ClassBalanceConfig"/>, и всё, что стенд собирает «по норме», обязано читать её оттуда.
    /// </summary>
    /// <remarks>
    /// Тест заведён 2026-08-01 по следам расхождения: <c>SyntheticUnits</c> держал собственную таблицу
    /// целевого DPS (Танк 60, Убийца 144, Дальник 120) при живой норме в конфиге (55 / 154 / 132). Эталонные
    /// НАПАРНИКИ в отрядных бенчах собирались по одной таблице, а испытуемые киты судились по другой — то
    /// есть замер сравнивал контент не с той линейкой, которую показывал в отчёте.
    /// <para>Комментарий такое не удержал бы: нарушение живёт в файле стенда, а норма — в файле данных, и
    /// вторая сторона шва о расхождении не узнаёт. Поэтому инвариант держит тест.</para>
    /// </remarks>
    public sealed class ClassNormOwnershipTests
    {
        /// <summary>Темп атаки манекена — тот же, что зашит в стенде: DPS раскладывается на урон и темп.</summary>
        private const float ReferenceAttackSpeed = 0.75f;

        private static ClassBalanceConfig MakeConfig()
        {
            var config = ScriptableObject.CreateInstance<ClassBalanceConfig>();
            var so = new UnityEditor.SerializedObject(config);
            so.FindProperty("_baseHp").floatValue = 2000f;
            so.FindProperty("_baseMoveSpeed").floatValue = 3f;
            so.FindProperty("_baseDps").floatValue = 100f;
            so.FindProperty("_baseHps").floatValue = 100f;

            UnityEditor.SerializedProperty profiles = so.FindProperty("_profiles");
            profiles.arraySize = 2;
            SetProfile(profiles.GetArrayElementAtIndex(0), UnitClass.Tank, hp: 1.5f, move: 0.85f,
                armor: 120f, dps: 0.5f, heal: 0f);
            SetProfile(profiles.GetArrayElementAtIndex(1), UnitClass.Healer, hp: 0.5f, move: 0.75f,
                armor: 20f, dps: 0.5f, heal: 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static void SetProfile(UnityEditor.SerializedProperty element, UnitClass unitClass,
            float hp, float move, float armor, float dps, float heal)
        {
            element.FindPropertyRelative("Class").enumValueIndex = (int)unitClass;
            element.FindPropertyRelative("HpMult").floatValue = hp;
            element.FindPropertyRelative("MoveSpeedMult").floatValue = move;
            element.FindPropertyRelative("ArmorBudget").floatValue = armor;
            element.FindPropertyRelative("DpsMult").floatValue = dps;
            element.FindPropertyRelative("HealMult").floatValue = heal;
        }

        [Test]
        public void ReferenceAlly_DealsExactlyTheClassDpsNorm()
        {
            ClassBalanceConfig config = MakeConfig();
            try
            {
                RuntimeUnit ally = SyntheticUnits.ReferenceAlly(UnitClass.Tank, config, 0, Vector2.zero);
                float dps = ally.Stats.Get(StatType.AutoAttackDamage) * ally.Stats.Get(StatType.AttackSpeed);

                Assert.That(dps, Is.EqualTo(config.GetDpsNorm(UnitClass.Tank)).Within(0.01f),
                    "Эталонный напарник обязан бить ровно по норме своего класса из ClassBalanceConfig.");
                Assert.That(ally.Stats.Get(StatType.AttackSpeed), Is.EqualTo(ReferenceAttackSpeed).Within(0.001f),
                    "Темп манекена — общий для стенда; норма раскладывается на урон и темп.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ReferenceAlly_FollowsConfigWhenNormMoves()
        {
            ClassBalanceConfig config = MakeConfig();
            try
            {
                var so = new UnityEditor.SerializedObject(config);
                so.FindProperty("_baseDps").floatValue = 200f;
                so.ApplyModifiedPropertiesWithoutUndo();

                RuntimeUnit ally = SyntheticUnits.ReferenceAlly(UnitClass.Tank, config, 0, Vector2.zero);
                float dps = ally.Stats.Get(StatType.AutoAttackDamage) * ally.Stats.Get(StatType.AttackSpeed);

                Assert.That(dps, Is.EqualTo(100f).Within(0.01f),
                    "Норма поехала — манекен обязан поехать вместе с ней, иначе линейка мерит вчерашнее.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void HealNorm_IsZeroForClassesWithoutHealingInTheirRole()
        {
            ClassBalanceConfig config = MakeConfig();
            try
            {
                Assert.That(config.GetHealNorm(UnitClass.Healer), Is.EqualTo(100f).Within(0.01f));
                Assert.That(config.GetHealNorm(UnitClass.Tank), Is.EqualTo(0f),
                    "Класс без лечения в роли получает норму 0 — иначе стенд ждёт хила от Танка.");
                Assert.That(config.GetHealNorm(UnitClass.Assassin), Is.EqualTo(0f),
                    "Класса нет в таблице профилей — норма лечения 0, а не эталон.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
