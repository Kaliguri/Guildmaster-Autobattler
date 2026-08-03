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
    /// Роли цвета юнита работоспособны. Юнит хранит РОЛИ, а не цвета: оттенок свечения
    /// (<see cref="UnitTone"/>) и ступень приглушения тела (<see cref="BodyShade"/>), а значения живут в
    /// палитре проекта.
    /// <para><b>Здесь проверяется только то, что роль превратится в цвет</b> — что она названа в палитре
    /// и что тинт остаётся умножением. КАК расставлять роли по контенту, тест не решает: правило
    /// «делящие спрайт обязаны различаться цветом» отменено 03.08.2026, см. комментарий в конце файла.</para>
    /// <para>Почему тестом, а не комментарием: перечисление живёт в коде, а значения — в USS-палитре, и
    /// разъехаться они могут молча — юнит просто засветит пурпуром.</para>
    /// </summary>
    public sealed class UnitTintPolicyTests
    {
        private static GuildmasterPalette Palette()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(GuildmasterPalette)}");
            Assert.AreEqual(1, guids.Length, "Ожидается ровно один снимок палитры проекта.");
            var palette = AssetDatabase.LoadAssetAtPath<GuildmasterPalette>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.IsNotNull(palette);
            return palette;
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

        // Трёх проверок здесь больше НЕТ, и это решение, а не упущение:
        //
        //   SharedArt_KeepsExactlyOneUnshadedOriginal — ровно один без приглушения на группу;
        //   SharedArt_ShadesDifferFromEachOther       — у делящих арт ступени попарно различны;
        //   OwnArt_IsNeverShaded                      — владелец своего арта не красится вовсе.
        //
        // Первые две требовали, чтобы юниты на общем спрайте различались цветом, и выполняться не могли:
        // ступеней всего четыре, а один только лист гоблина делят ШЕСТЬ юнитов (BanditAssassin,
        // GoblinCommander, GoblinCutthroat, GoblinGrunt, GoblinWarrior, GoblinWolfrider) — шесть различий
        // из четырёх значений не собираются, и «починка» свелась бы к подгонке чисел под зелёный.
        // Третья запрещала обратное: красить того, кому не с кем путаться.
        //
        // Правило отменено Максом 03.08.2026 целиком: одинаковые спрайт и цвет допустимы, а красить
        // одиночку — его дело, не теста. Тинт из обязанности стал возможностью.
        //
        // Осталось ровно то, без чего механика сломается молча: ступень обязана быть названа в палитре,
        // и умножение обязано оставаться умножением (осветлить тинтом нельзя, в грязь уводить незачем).
    }
}
