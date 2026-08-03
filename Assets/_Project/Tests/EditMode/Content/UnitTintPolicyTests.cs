using System;
using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Политика цвета юнита (решения Макса 30.07.2026). Юнит хранит РОЛИ, а не цвета: оттенок свечения
    /// (<see cref="UnitTone"/>) и ступень приглушения тела (<see cref="BodyShade"/>). Значения живут в
    /// палитре проекта, поэтому здесь проверяется не «красиво ли», а что роли существуют и расставлены по
    /// правилу.
    /// <para>Правило тинта: он УМНОЖАЕТСЯ на готовый цветной арт, значит перекрасить им персонажа нельзя —
    /// он годится ровно на то, чтобы развести юнитов, делящих один спрайт. Один из группы остаётся без
    /// приглушения (оригинал), остальные берут ступень; у владельца своего арта приглушения нет.</para>
    /// <para>Почему тестом, а не комментарием: спрайт назначается в ПРЕФАБЕ (у части юнитов вообще
    /// наследуется от базового <c>UnitView</c>), роль — в SO, и ни одна сторона шва не видит вторую.</para>
    /// </summary>
    public sealed class UnitTintPolicyTests
    {
        private static List<UnitData> AllUnits() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitData)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UnitData>)
                .Where(u => u != null)
                .OrderBy(u => u.name)
                .ToList();

        private static GuildmasterPalette Palette()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(GuildmasterPalette)}");
            Assert.AreEqual(1, guids.Length, "Ожидается ровно один снимок палитры проекта.");
            var palette = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(palette);
            return palette;
        }

        /// <summary>
        /// Подпись арта юнита: по ней и определяется, делит ли он спрайты с кем-то ещё. Составное тело
        /// подписывается ВСЕМИ своими частями (два скелетных юнита с разным набором частей — разный арт),
        /// одиночное — спрайтом узла <c>Visual Sprites/Body</c>, тем же, что читает каталог визуалов.
        /// Пусто — арта нет (юнит на дефолтном префабе презентера), такой из проверки выпадает.
        /// </summary>
        private static string ArtKey(UnitData unit)
        {
            GameObject prefab = unit.ViewPrefab;
            if (prefab == null) return null;

            var skeletal = prefab.GetComponentInChildren<SkeletalBodyVisual>(true);
            if (skeletal != null && skeletal.Renderers.Count > 0)
            {
                var keys = skeletal.Renderers
                    .Where(p => p != null && p.sprite != null)
                    .Select(SpriteKey)
                    .OrderBy(s => s)
                    .ToArray();
                if (keys.Length > 0) return string.Join("|", keys);
            }

            Transform body = prefab.transform.Find("Visual Sprites/Body");
            var sr = body != null ? body.GetComponent<SpriteRenderer>() : null;
            return sr != null && sr.sprite != null ? SpriteKey(sr) : null;
        }

        private static string SpriteKey(SpriteRenderer sr) =>
            $"{AssetDatabase.GetAssetPath(sr.sprite)}#{sr.sprite.name}";

        private static Dictionary<string, List<UnitData>> ByArt()
        {
            var groups = new Dictionary<string, List<UnitData>>();
            foreach (UnitData unit in AllUnits())
            {
                string key = ArtKey(unit);
                if (string.IsNullOrEmpty(key)) continue;
                if (!groups.TryGetValue(key, out List<UnitData> list))
                    groups[key] = list = new List<UnitData>();
                list.Add(unit);
            }

            return groups;
        }

        /// <summary>
        /// Каждая роль умеет превратиться в цвет. Это единственное место, где перечисление в коде и имена
        /// токенов в USS могут разъехаться: элемент добавили, роль в палитру не завели — и юнит на арене
        /// светит пурпуром.
        /// </summary>
        [Test]
        public void EveryRole_ExistsInThePalette()
        {
            GuildmasterPalette palette = Palette();

            foreach (UnitTone tone in Enum.GetValues(typeof(UnitTone)).Cast<UnitTone>())
            {
                string token = UnitColorRoles.TokenOf(tone);
                Assert.IsNotNull(token, $"оттенок {tone} не назван в UnitColorRoles.TokenOf");
                Assert.IsTrue(palette.TryGet(token, out _),
                    $"в палитре нет роли '{token}' (оттенок {tone}). Пересобери снимок: " +
                    "Alebardium → Дизайн-система → Пересобрать палитру.");
            }

            foreach (BodyShade shade in Enum.GetValues(typeof(BodyShade)).Cast<BodyShade>())
            {
                if (shade == BodyShade.None) continue;   // «не красим» цвета не имеет и не должно
                string token = UnitColorRoles.TokenOf(shade);
                Assert.IsNotNull(token, $"ступень {shade} не названа в UnitColorRoles.TokenOf");
                Assert.IsTrue(palette.TryGet(token, out _),
                    $"в палитре нет роли '{token}' (ступень {shade}).");
            }
        }

        /// <summary>
        /// Ступени приглушения обязаны БЫТЬ приглушениями: тинт умножается на арт, поэтому канал выше 1
        /// не осветлит (клампится), а слишком низкий уводит тело в грязь. Прозрачность тинтом не задаётся —
        /// её занимает стелс.
        /// </summary>
        [Test]
        public void Shades_StayWithinWhatMultiplyingCanDo()
        {
            const float minChannel = 0.35f;
            GuildmasterPalette palette = Palette();

            foreach (BodyShade shade in Enum.GetValues(typeof(BodyShade)).Cast<BodyShade>())
            {
                Color c = UnitColorRoles.Shade(palette, shade);
                Assert.AreEqual(1f, c.a, 1e-3f, $"{shade}: alpha держим 1, прозрачность — не работа тинта.");
                foreach ((float channel, string name) in new[] { (c.r, "r"), (c.g, "g"), (c.b, "b") })
                {
                    Assert.LessOrEqual(channel, 1f,
                        $"{shade}: канал {name} = {channel} — тинт умножает, осветлить им нельзя.");
                    Assert.GreaterOrEqual(channel, minChannel,
                        $"{shade}: канал {name} = {channel} гасит арт в грязь. Нужна другая ОКРАСКА, " +
                        "а не затемнение — это работа Palette Remapper.");
                }
            }
        }

        [Test]
        public void OwnArt_IsNeverShaded()
        {
            foreach (KeyValuePair<string, List<UnitData>> group in ByArt())
            {
                if (group.Value.Count != 1) continue;
                UnitData unit = group.Value[0];
                Assert.AreEqual(BodyShade.None, unit.BodyShade,
                    $"{unit.name} — единственный владелец своего арта ({group.Key}), приглушение ему не нужно: " +
                    "разводить не с кем, а умножение только глушит краски художника.");
            }
        }

        // Двух проверок здесь больше НЕТ, и это решение, а не упущение:
        //
        //   SharedArt_KeepsExactlyOneUnshadedOriginal — ровно один без приглушения на группу;
        //   SharedArt_ShadesDifferFromEachOther       — у делящих арт ступени попарно различны.
        //
        // Обе требовали, чтобы юниты на общем спрайте различались цветом. Правило отменено Максом
        // 03.08.2026: одинаковые спрайт и цвет допустимы. Оно и не могло выполняться — ступеней всего
        // четыре, а один только лист гоблина делят ШЕСТЬ юнитов (BanditAssassin, GoblinCommander,
        // GoblinCutthroat, GoblinGrunt, GoblinWarrior, GoblinWolfrider). Шесть различий из четырёх
        // значений не собираются, и «починка» свелась бы к подгонке чисел под зелёный.
        //
        // Тинт остаётся ВОЗМОЖНОСТЬЮ развести похожих, но перестал быть обязанностью. Проверки самой
        // механики выше — что ступень названа в палитре и что умножение не осветляет — в силе.
    }
}
