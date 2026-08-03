using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Снимок палитры для мира. Цвета живут в <c>UI/Theme/tokens.*.uss</c>, но карта, VFX и перекрасчик
    /// рисуются мимо UI Toolkit и читают их через <see cref="GuildmasterPalette"/>. Снимок собирается
    /// меню — а значит может отстать от исходника молча: правку USS видно сразу в интерфейсе, а в мире
    /// она просто не появится.
    /// <para>Тест держит их сцепленными. Красный тут значит ровно одно: <c>Alebardium → Дизайн-система →
    /// Пересобрать палитру</c>.</para>
    /// </summary>
    [TestFixture]
    public class PaletteSnapshotTests
    {
        /// <summary>Роли, без которых карта акта не нарисуется — их отсутствие ловим отдельно и по имени.</summary>
        private static readonly string[] MapRoles =
        {
            "--gm-color-map-node-backing",
            "--gm-color-map-node-rim",
            "--gm-color-map-current-marker",
            "--gm-color-map-icon-shadow",
            "--gm-color-map-icon-light",
            "--gm-color-map-path-idle",
            "--gm-color-map-path-travelled",
            "--gm-color-map-path-available",
            "--gm-color-map-pawn",
        };

        /// <summary>
        /// Роли боевого фидбэка: их называют <c>CombatFeelConfig</c> и <c>CombatColorPalette</c>, своих
        /// <c>Color</c>-полей у обоих больше нет. Отсутствие роли — это вспышка удара пурпуром, поэтому
        /// список держится здесь по именам, а не выводится из кода: вывод сломался бы вместе с кодом.
        /// </summary>
        private static readonly string[] CombatRoles =
        {
            "--gm-color-combat-flash-physical",
            "--gm-color-combat-flash-magical",
            "--gm-color-combat-flash-true",
            "--gm-color-combat-flash-poison",
            "--gm-color-combat-flash-light",
            "--gm-color-combat-flash-dark",
            "--gm-color-combat-flash-neutral",
            "--gm-color-combat-heal",
            "--gm-color-combat-hp-ally",
            "--gm-color-combat-hp-enemy",
            "--gm-color-combat-shield",
            "--gm-color-combat-overbright",
            "--gm-color-combat-hologram",
            "--gm-color-combat-cut",
        };

        private static GuildmasterPalette LoadAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(PaletteSnapshotBuilder.AssetPath);
            Assert.IsNotNull(asset, $"нет снимка палитры: {PaletteSnapshotBuilder.AssetPath}");
            return asset;
        }

        [Test]
        public void Снимок_совпадает_с_токенами_в_USS()
        {
            GuildmasterPalette.Entry[] fresh = PaletteSnapshotBuilder.Collect(out string problem);
            Assert.IsNull(problem, problem);
            Assert.IsNotNull(fresh, "не удалось прочитать токены");

            GuildmasterPalette asset = LoadAsset();
            var stored = new Dictionary<string, Color>(asset.Entries.Length);
            foreach (GuildmasterPalette.Entry e in asset.Entries) stored[e.Token] = e.Color;

            var stale = new List<string>();
            foreach (GuildmasterPalette.Entry e in fresh)
            {
                if (!stored.TryGetValue(e.Token, out Color have)) { stale.Add($"{e.Token} (нет в снимке)"); continue; }
                if ((Color32)have is var h && (Color32)e.Color is var w && (h.r != w.r || h.g != w.g || h.b != w.b))
                    stale.Add($"{e.Token}: в снимке #{ColorUtility.ToHtmlStringRGB(have)}, " +
                              $"в USS #{ColorUtility.ToHtmlStringRGB(e.Color)}");
            }

            Assert.IsEmpty(stale,
                "снимок палитры отстал от tokens.*.uss — пересобери его через " +
                "Alebardium → Дизайн-система → Пересобрать палитру:\n" + string.Join("\n", stale));
        }

        [Test]
        public void Все_роли_карты_есть_в_палитре()
        {
            GuildmasterPalette asset = LoadAsset();

            foreach (string role in MapRoles)
                Assert.IsTrue(asset.TryGet(role, out _),
                    $"в палитре нет роли '{role}' — карта нарисует её пурпуром и скажет об этом в логе");
        }

        [Test]
        public void Все_роли_боевого_фидбэка_есть_в_палитре()
        {
            GuildmasterPalette asset = LoadAsset();

            foreach (string role in CombatRoles)
                Assert.IsTrue(asset.TryGet(role, out _),
                    $"в палитре нет роли '{role}' — бой вспыхнет пурпуром и скажет об этом в логе");
        }
    }
}
