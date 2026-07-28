using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Валидация контент-каркаса (вики «13» §8, правила 1–3, 5): формат/уникальность id, полнота
    /// реестра, отсутствие null в <c>[SerializeReference]</c>-списках, кросс-инварианты диапазонов.
    /// Красный тест = ошибка данных в коммите, а не флейк — правится контент/Sync, не тест.
    /// </summary>
    public sealed class ContentValidationTests
    {
        // Своей регулярки формата id здесь нет: правило принадлежит ContentDomains — он же его
        // применяет, когда id генерирует. Копия разъехалась бы на первой правке формата.

        private static List<ContentDefinition> AllContent() => ContentIdUtility.FindAll();

        // --- §8 правило 1: id непустой, формат domain.name, домен по типу, без дубликатов ---

        [Test]
        public void AllContent_HasValidId()
        {
            foreach (ContentDefinition def in AllContent())
            {
                string assetPath = AssetDatabase.GetAssetPath(def);
                Assert.IsFalse(string.IsNullOrEmpty(def.Id), $"Empty id: {assetPath}");
                Assert.IsTrue(ContentDomains.IsValidId(def.Id), $"Id '{def.Id}' not in domain.name format: {assetPath}");

                string expectedDomain = ContentDomains.GetDomain(def.GetType());
                Assert.IsTrue(def.Id.StartsWith(expectedDomain + "."),
                    $"Id '{def.Id}' domain mismatch: type {def.GetType().Name} expects '{expectedDomain}.' ({assetPath})");
            }
        }

        [Test]
        public void AllContent_IdsUnique()
        {
            var byId = new Dictionary<string, string>();
            foreach (ContentDefinition def in AllContent())
            {
                string path = AssetDatabase.GetAssetPath(def);
                if (byId.TryGetValue(def.Id, out string other))
                    Assert.Fail($"Duplicate content id '{def.Id}': {path} and {other}");
                byId[def.Id] = path;
            }
        }

        // --- §8 правило 2: ContentDatabase полон, без null, актуален после Sync ---

        [Test]
        public void ContentDatabase_ExistsAndComplete()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ContentDatabase)}");
            Assert.AreEqual(1, guids.Length,
                "Ровно один ContentDatabase.asset ожидается (Alebardium/Data/Sync Content Database).");

            var db = AssetDatabase.LoadAssetAtPath<ContentDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(db);

            Assert.IsFalse(db.Entries.Any(e => e == null), "ContentDatabase содержит null-ссылку.");

            var dbIds = db.Entries.Select(e => e.Id).OrderBy(x => x).ToArray();
            var projectIds = AllContent().Select(d => d.Id).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(projectIds, dbIds,
                "ContentDatabase не актуален: запусти Alebardium/Data/Sync Content Database.");
        }

        // --- §8 правило 3: [SerializeReference]-списки без null (следы missing types) ---

        [Test]
        public void EffectComponents_NoNullElements()
        {
            foreach (ContentDefinition def in AllContent())
            {
                if (def is not EffectData effect || effect.Components == null) continue;

                for (int i = 0; i < effect.Components.Length; i++)
                {
                    Assert.IsNotNull(effect.Components[i],
                        $"Effect '{effect.Id}' has a null [SerializeReference] component at [{i}] " +
                        $"(вероятно missing type): {AssetDatabase.GetAssetPath(def)}");
                }
            }
        }

        // --- §8 правило 4: обязательные ключи локализации по доменам ---

        [Test]
        public void RequiredLocalizationKeys_Exist()
        {
            foreach (ContentDefinition def in AllContent())
            {
                IReadOnlyList<string> missing = ContentLocalization.MissingKeys(def);
                Assert.IsEmpty(missing,
                    $"Content '{def.Id}' ({AssetDatabase.GetAssetPath(def)}) missing required localization key(s): " +
                    $"[{string.Join(", ", missing)}] — open it in the inspector and press 'Create missing keys'.");
            }
        }

        // --- §8 правило 5: диапазоны и кросс-инварианты ---

        [Test]
        public void Relics_AiProfileRanges()
        {
            foreach (RelicData relic in AllContent().OfType<RelicData>())
            {
                AIProfile ai = relic.Ai;
                if (ai == null) continue;

                // Гистерезис осмыслен только когда механика включена; у выключенных retreat/kite
                // значения нулевые (default) и порядок не важен.
                if (ai.Retreat.Enabled)
                    Assert.Less(ai.Retreat.FleeAtHpPct, ai.Retreat.ReturnAtHpPct,
                        $"Relic '{relic.Id}': при включённом Retreat FleeAtHpPct должен быть < ReturnAtHpPct (гистерезис).");
                if (ai.Kite.Enabled)
                    Assert.Less(ai.Kite.FleeDist, ai.Kite.FallbackDist,
                        $"Relic '{relic.Id}': при включённом Kite FleeDist должен быть < FallbackDist.");
                Assert.That(ai.PassiveThresholdPct, Is.InRange(0f, 1f),
                    $"Relic '{relic.Id}': PassiveThresholdPct вне [0..1].");
            }
        }

        /// <summary>
        /// База HP и скорости передвижения принадлежит боевому классу (ГДД «Боевая система»: «Базу HP и
        /// скорости передвижения задаёт класс»), а <c>ClassBalanceConfig</c> кладёт её первой Override-группой.
        /// Значение из <c>StatsConfig._defaults</c> при этом до юнита не доживало никогда — но выглядело
        /// правдой при чтении конфига, и уже разошлось с классовым якорем (1200 против 2000×множитель).
        /// Дефолты сняты; тест держит их снятыми, чтобы второй владелец не отрос заново.
        /// </summary>
        [Test]
        public void StatsConfig_DoesNotOwnBaseHpOrMoveSpeed()
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(StatsConfig)}"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<StatsConfig>(AssetDatabase.GUIDToAssetPath(guid));
                var so  = new SerializedObject(cfg);
                SerializedProperty defaults = so.FindProperty("_defaults");

                for (int i = 0; i < defaults.arraySize; i++)
                {
                    var stat = (StatType)defaults.GetArrayElementAtIndex(i).FindPropertyRelative("Stat").enumValueIndex;
                    Assert.That(stat, Is.Not.EqualTo(StatType.MaxHP).And.Not.EqualTo(StatType.MoveSpeed),
                        $"StatsConfig ({AssetDatabase.GetAssetPath(cfg)}) задаёт дефолт '{stat}'. " +
                        "Базу HP и скорости держит боевой класс — здесь она была бы вторым владельцем.");
                }
            }
        }

        /// <summary>
        /// Раз базы в <c>StatsConfig</c> больше нет, единственный её источник — класс. Юнит без класса
        /// собрался бы с нулевым HP и умер на спавне, поэтому проверяем: класс задан у каждого.
        /// </summary>
        [Test]
        public void EveryUnit_DeclaresCombatClass()
        {
            foreach (UnitData unit in AllContent().OfType<UnitData>())
                Assert.That(System.Enum.IsDefined(typeof(UnitClass), unit.CombatClass), Is.True,
                    $"'{unit.Id}': боевой класс не задан, а базу HP и скорости даёт именно он.");
        }

        // Тест клампа скорости атаки снят вместе с самим клампом (решение 2026-07-28): симуляция его
        // не применяла, поля в StatsConfig врали, а потолок темпа бил по киту, чья фантазия — разгон.

        // --- §8 правило 7: ссылочная целостность по id — враги энкаунтера существуют ---

        [Test]
        public void Encounters_EnemyIdsExist()
        {
            var enemyIds = new HashSet<string>(AllContent().OfType<EnemyData>().Select(e => e.Id));

            foreach (EncounterData enc in AllContent().OfType<EncounterData>())
            {
                if (enc.Units == null) continue;
                foreach (EncounterUnit u in enc.Units)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(u.EnemyId),
                        $"Encounter '{enc.Id}' has an empty EnemyId ({AssetDatabase.GetAssetPath(enc)}).");
                    Assert.IsTrue(enemyIds.Contains(u.EnemyId),
                        $"Encounter '{enc.Id}' references unknown enemy id '{u.EnemyId}' " +
                        $"({AssetDatabase.GetAssetPath(enc)}).");
                }
            }
        }

        // --- §8 правило 7: BattlePreset — энкаунтер задан, слоты ростера имеют релик (кит) ---

        [Test]
        public void BattlePresets_Valid()
        {
            foreach (BattlePresetData preset in AllContent().OfType<BattlePresetData>())
            {
                string path = AssetDatabase.GetAssetPath(preset);
                Assert.IsNotNull(preset.Encounter, $"Battle preset '{preset.Id}' has no encounter ({path}).");
                Assert.IsNotNull(preset.Roster, $"Battle preset '{preset.Id}' has a null roster ({path}).");

                foreach (PlayerSlot slot in preset.Roster)
                    Assert.IsNotNull(slot.Relic,
                        $"Battle preset '{preset.Id}' has a roster slot with no relic — the relic slot must always be " +
                        $"filled (relic.base for an empty vessel) ({path}).");
            }
        }

        // --- §8 правило 7: последствия ивента ссылаются на существующий контент ---

        [Test]
        public void TextEvents_EffectContentIdsExist()
        {
            // Ловит ровно тот класс багов, что был у демо-ивента: выбор выдавал relic.merchant_trinket,
            // которой в контенте нет — награда уходила в пустоту, и молча.
            var ids = new HashSet<string>(AllContent().Select(c => c.Id));

            foreach (TextEventData ev in AllContent().OfType<TextEventData>())
            {
                string path = AssetDatabase.GetAssetPath(ev);
                Assert.IsTrue(ev.Choices.Count >= 2,
                    $"Text event '{ev.Id}' must offer at least 2 choices ({path}).");

                for (int i = 0; i < ev.Choices.Count; i++)
                {
                    foreach (EventEffect effect in ev.Choices[i].Effects)
                    {
                        bool needsContent = effect.Kind is EventEffectKind.GrantRelic
                                                        or EventEffectKind.RemoveRelic
                                                        or EventEffectKind.GrantItem;
                        if (!needsContent) continue;

                        Assert.IsFalse(string.IsNullOrEmpty(effect.ContentId),
                            $"Text event '{ev.Id}' choice {i}: effect {effect.Kind} has an empty content id ({path}).");
                        Assert.IsTrue(ids.Contains(effect.ContentId),
                            $"Text event '{ev.Id}' choice {i}: effect {effect.Kind} references unknown content id " +
                            $"'{effect.ContentId}' ({path}).");
                    }
                }
            }
        }
    }
}
