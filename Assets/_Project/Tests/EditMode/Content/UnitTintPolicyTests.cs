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
    /// Роли цвета юнита работоспособны. Юнит хранит РОЛЬ, а не цвет, и роль у него ровно одна —
    /// <see cref="UnitTone"/>: ею красится и тело, и всё, чем юнит светит. Значения живут в палитре проекта.
    /// <para><b>Здесь проверяется только то, что роль превратится в цвет</b> — что она названа в палитре.
    /// КАК расставлять роли по контенту, тест не решает: правило «делящие спрайт обязаны различаться
    /// цветом» отменено 03.08.2026, а ступень приглушения снята целиком 05.08.2026 — см. конец файла.</para>
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
        }

        /// <summary>
        /// ЕДИНЫЙ ИСТОЧНИК: цвет тела и цвет эффектов — это один и тот же оттенок (решение Макса
        /// 05.08.2026). Тест держит шов между двумя потребителями, которые спрашивают цвет порознь —
        /// бой и карточка инвентаря; разъехавшись, они дали бы юниту разный цвет на двух экранах, и
        /// заметить это можно было бы только глазами, переключаясь между ними.
        /// </summary>
        [Test]
        public void BodyTint_IsTheSameToneAsTheEffects()
        {
            GuildmasterPalette palette = Palette();

            foreach (UnitTone tone in Enum.GetValues(typeof(UnitTone)).Cast<UnitTone>())
                Assert.AreEqual(UnitColorRoles.Tone(palette, tone), UnitColorRoles.Body(palette, tone),
                    $"тело и эффекты разошлись на оттенке {tone} — у цвета юнита снова два владельца.");
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
        // Четвёртой проверки — Shades_StayWithinWhatMultiplyingCanDo — тоже нет: ступеней приглушения
        // больше не существует (05.08.2026), тело красится оттенком самого юнита. Проверять «умножение
        // осталось умножением» теперь не на чем: тон приходит из палитры, где значения больше единицы
        // не живут по её собственному правилу.
        //
        // Осталось ровно то, без чего механика сломается молча: роль обязана быть названа в палитре, и
        // цвет тела обязан совпадать с цветом эффектов — иначе у юнита снова два владельца цвета.
    }
}
