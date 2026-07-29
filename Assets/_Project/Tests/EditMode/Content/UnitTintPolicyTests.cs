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
    /// Политика тинта тела (решение Макса 30.07.2026). Тинт — УМНОЖЕНИЕ на готовый цветной арт, поэтому
    /// перекрасить им персонажа нельзя (синий рыцарь под оранжевым тинтом становится тёмно-серым, а не
    /// огненным). Его единственная работа — развести юнитов, которые делят один спрайт: у своего арта тинт
    /// остаётся White, у повторок отличается.
    /// <para>Правило живёт в тесте, а не в комментарии: спрайт назначается в ПРЕФАБЕ, цвет — в SO, и ни одна
    /// из двух сторон шва не видит вторую. Раньше здесь стоял дев-фолбэк «оттенок от хеша имени», и он красил
    /// восемь юнитов цветом, который никто не выбирал.</para>
    /// </summary>
    public sealed class UnitTintPolicyTests
    {
        private const float MinChannel = 0.35f;   // ниже — тело уходит в грязь и перестаёт читаться

        private static List<UnitData> AllUnits() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitData)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<UnitData>)
                .Where(u => u != null)
                .OrderBy(u => u.name)
                .ToList();

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
            if (skeletal != null && skeletal.Parts.Count > 0)
            {
                var keys = skeletal.Parts
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

        [Test]
        public void Resolver_ReturnsTheAuthoredTintUntouched()
        {
            foreach (UnitData unit in AllUnits())
                Assert.AreEqual(unit.Tint, unit.ResolveBodyTint(),
                    $"{unit.name}: резолвер тинта изобретает цвет вместо заданного в ассете. " +
                    "Играет ассет, а не код — иначе вид юнита нельзя ни выбрать, ни увидеть в инспекторе.");
        }

        [Test]
        public void OwnArt_IsNeverTinted()
        {
            foreach (KeyValuePair<string, List<UnitData>> group in ByArt())
            {
                if (group.Value.Count != 1) continue;
                UnitData unit = group.Value[0];
                Assert.AreEqual(Color.white, unit.Tint,
                    $"{unit.name} — единственный владелец своего арта ({group.Key}), тинт ему не нужен: " +
                    "разводить не с кем, а умножение только глушит краски художника.");
            }
        }

        [Test]
        public void SharedArt_KeepsExactlyOneUntintedOriginal()
        {
            foreach (KeyValuePair<string, List<UnitData>> group in ByArt())
            {
                if (group.Value.Count < 2) continue;
                UnitData[] plain = group.Value.Where(u => u.Tint == Color.white).ToArray();
                Assert.AreEqual(1, plain.Length,
                    $"Арт {group.Key} делят {group.Value.Count} юнита ({string.Join(", ", group.Value.Select(u => u.name))}), " +
                    $"а без тинта из них {plain.Length}. Ровно один показывает арт как есть — он оригинал; " +
                    "остальные подкрашиваются, чтобы их различали на арене.");
            }
        }

        [Test]
        public void SharedArt_TintsDifferFromEachOther()
        {
            foreach (KeyValuePair<string, List<UnitData>> group in ByArt())
            {
                if (group.Value.Count < 2) continue;
                var seen = new Dictionary<Color, string>();
                foreach (UnitData unit in group.Value)
                {
                    if (seen.TryGetValue(unit.Tint, out string other))
                        Assert.Fail($"{unit.name} и {other} делят и арт ({group.Key}), и тинт {unit.Tint} — " +
                                    "то есть на арене неразличимы.");
                    seen[unit.Tint] = unit.name;
                }
            }
        }

        /// <summary>
        /// Цвет эффектов задан у КАЖДОГО юнита явно. Проверка появилась вместе с White-тинтом: у
        /// <c>ResolveVfxColor</c> незаданный (White) цвет уводит в тинт тела, а тот теперь честно бывает White —
        /// то есть цепочка фолбэков вела бы к ровно белому эффекту, который даже не пробивает порог bloom (1.0).
        /// Цвет эффектов — авторское решение, и его отсутствие должно падать здесь, а не тихо белить искры.
        /// </summary>
        [Test]
        public void VfxColor_IsAuthoredExplicitly()
        {
            foreach (UnitData unit in AllUnits())
                Assert.AreNotEqual(Color.white, unit.VfxColor,
                    $"{unit.name}: цвет эффектов не задан. Норма — насыщенность 70–90% и яркость выше 1 " +
                    "(HDR ловит bloom); нужен белый — берётся около-белый вроде 2.6/2.5/2.4.");
        }

        [Test]
        public void Tint_StaysWithinWhatMultiplyingCanDo()
        {
            foreach (UnitData unit in AllUnits())
            {
                Color t = unit.Tint;
                Assert.AreEqual(1f, t.a, 1e-3f,
                    $"{unit.name}: прозрачность тела задаётся не тинтом (её занимает стелс) — alpha держим 1.");
                foreach ((float channel, string name) in new[] { (t.r, "r"), (t.g, "g"), (t.b, "b") })
                {
                    Assert.LessOrEqual(channel, 1f,
                        $"{unit.name}: канал {name} = {channel} — тинт умножает, осветлить им нельзя, " +
                        "значение выше 1 просто клампится.");
                    Assert.GreaterOrEqual(channel, MinChannel,
                        $"{unit.name}: канал {name} = {channel} гасит арт в грязь. " +
                        "Нужна другая ОКРАСКА, а не затемнение — это работа Palette Remapper.");
                }
            }
        }
    }
}
