using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Валидация анимаций Фазы 4 (вики «13» §8 п.8): обязательные слоты клипов заполнены; Attack/Skill-клипы
    /// несут маркер контакта в пределах клипа; <c>AbilityData.VisualSlot</c> указывает на непустой слот.
    /// Красный тест = битый визуал в коммите (правится ассет/клип, не тест).
    /// </summary>
    public sealed class AnimationValidationTests
    {
        private static UnitVisual[] AllVisuals() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitVisual)}")
                .Select(g => AssetDatabase.LoadAssetAtPath<UnitVisual>(AssetDatabase.GUIDToAssetPath(g)))
                .ToArray();

        private static UnitData[] AllUnits() =>
            AssetDatabase.FindAssets($"t:{nameof(UnitData)}")
                .Select(g => AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(u => u != null)
                .ToArray();

        [Test]
        public void HitDamageShares_MatchMarkerCount()
        {
            // Доли урона задаются каждому Удару лично и читаются ПО ИНДЕКСУ контакта. Список короче
            // разметки — часть Ударов молча уйдёт в полную силу; длиннее — лишние числа никогда не
            // сыграют, и автор будет крутить их, не понимая, почему ничего не меняется. Обе ошибки
            // не видны в игре, поэтому ловятся здесь.
            foreach (UnitData unit in AllUnits())
            {
                float[] shares = unit.HitDamageShares;
                if (shares == null || shares.Length == 0) continue;   // не задано = каждый Удар в полную силу

                int contacts = unit.Visual != null ? unit.Visual.AttackHitCount : 0;
                Assert.AreEqual(contacts, shares.Length,
                    $"{unit.name}: долей урона {shares.Length}, а контактов в клипе атаки {contacts} " +
                    $"({AssetDatabase.GetAssetPath(unit)}). Число долей обязано совпадать с числом маркеров.");
            }
        }

        [Test]
        public void Visuals_RequiredBaseSlotsFilled()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                string path = AssetDatabase.GetAssetPath(vis);
                foreach (UnitAnimationState state in new[]
                {
                    UnitAnimationState.Idle, UnitAnimationState.Run,
                    UnitAnimationState.Attack, UnitAnimationState.Death,
                })
                    Assert.IsNotNull(vis.Clip(state), $"UnitVisual '{vis.name}' missing {state} clip ({path}).");
            }
        }

        [Test]
        public void Visuals_AttackClipHasMarkerWithinClip()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                AnimationClip attack = vis.AttackClip;
                if (attack == null) continue; // покрыто RequiredBaseSlotsFilled
                string path = AssetDatabase.GetAssetPath(vis);

                float t = ClipMarkers.FirstHitTime(attack);
                Assert.GreaterOrEqual(t, 0f, $"UnitVisual '{vis.name}' Attack clip has no \"Marker\" event ({path}).");
                Assert.LessOrEqual(t, attack.length,
                    $"UnitVisual '{vis.name}' Attack marker at {t}s is past clip end {attack.length}s ({path}).");
            }
        }

        /// <summary>
        /// Маркер контакта не стоит в НУЛЕ. Ноль формально «внутри клипа», поэтому проверка выше его
        /// пропускает, а сим считает по нему долю замаха: `windup = hit / frames`, то есть удар без замаха.
        /// Ровно это случилось 30.07.2026, когда Монах переехал на Fantasy Warrior — у пака маркер лежал в
        /// нуле, и его авто-атака стала бить мгновенно, молча и без единой ошибки в консоли.
        /// </summary>
        [Test]
        public void Visuals_AttackMarkerIsNotAtFrameZero()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                AnimationClip attack = vis.AttackClip;
                if (attack == null) continue;
                if (ClipMarkers.FirstHitTime(attack) < 0f) continue;   // отсутствие покрыто тестом выше

                Assert.Greater(vis.AttackHitFrame, 0,
                    $"UnitVisual '{vis.name}': маркер контакта стоит на кадре 0 " +
                    $"({AssetDatabase.GetAssetPath(vis)}). Сим выведет из него нулевой замах — удар без " +
                    "подводки. Поставь маркер туда, где оружие реально достаёт цель.");
            }
        }

        /// <summary>
        /// Разметка взмаха бывает либо полной, либо никакой. Половина её — это не «часть данных», а
        /// неразмеченный клип: <c>ClipMarkers.StrikeWindowNormalized</c> отвечает на неё «окна нет», и
        /// автор, поставивший один маркер из двух, увидит ровно то же, что и не ставивший ничего —
        /// пропавшую дугу за клинком и форму удара, выходящую из ног. Ошибки при этом нигде нет.
        /// </summary>
        [Test]
        public void Visuals_StrikeWindow_IsWholeOrAbsent()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                AnimationClip attack = vis.AttackClip;
                if (attack == null) continue;

                bool hasStart = ClipMarkers.FirstTimeOf(attack, ClipMarkers.StrikeStartFunction) >= 0f;
                bool hasEnd   = ClipMarkers.FirstTimeOf(attack, ClipMarkers.StrikeEndFunction)   >= 0f;

                Assert.AreEqual(hasStart, hasEnd,
                    $"UnitVisual '{vis.name}': в клипе '{attack.name}' размечена ПОЛОВИНА взмаха " +
                    $"({ClipMarkers.StrikeStartFunction}={hasStart}, {ClipMarkers.StrikeEndFunction}={hasEnd}), " +
                    $"{AssetDatabase.GetAssetPath(vis)}. Взмах читается только целиком: половина = " +
                    "клип без дуги и без точки, откуда пришёл удар.");
            }
        }

        /// <summary>
        /// Контакт лежит ВНУТРИ размеченного взмаха. Маркер удара за пределами окна означает, что клинок
        /// достаёт цель тогда, когда по разметке он ещё собирается или уже возвращается: дуга нарисуется
        /// не там, где случился удар, и форма выйдет из точки, которой в этот момент не существовало.
        /// </summary>
        [Test]
        public void Visuals_HitMarker_LiesInsideStrikeWindow()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                AnimationClip attack = vis.AttackClip;
                if (attack == null) continue;
                if (!ClipMarkers.StrikeWindowNormalized(attack, out float from, out float to)) continue;

                float hit = ClipMarkers.HitNormalized(attack);
                Assert.IsTrue(hit >= from && hit <= to,
                    $"UnitVisual '{vis.name}': контакт клипа '{attack.name}' стоит на {hit:F3} " +
                    $"нормированного времени, а взмах размечен {from:F3}..{to:F3} " +
                    $"({AssetDatabase.GetAssetPath(vis)}).");
            }
        }

        /// <summary>
        /// У кита СО СКЕЛЕТНЫМ телом разметка взмаха ОБЯЗАТЕЛЬНА. Ему есть чем чертить дугу — оружие
        /// объявлено частью тела, — и отсутствие маркеров означает не «нет контента», а тихо потерянный
        /// язык ближнего боя: <c>UnitView</c> без окна взмаха не зовёт хук дуги ни разу, и на экране это
        /// выглядит как задумка. Ровно так дуги за клинком не существовало в игре до 04.08.2026.
        /// <para>
        /// Покадровый бестиарий сюда не попадает намеренно: у него тело — один спрайт, оружия как части
        /// нет, и дугу вести не от чего.
        /// </para>
        /// </summary>
        [Test]
        public void SkeletalUnits_AttackClips_CarryStrikeWindow()
        {
            var missing = new System.Collections.Generic.List<string>();

            foreach (UnitData unit in AllUnits())
            {
                GameObject view = unit.ViewPrefab;
                if (view == null) continue;
                if (view.GetComponentInChildren<Guildmaster.Presentation.Body.SkeletalBodyVisual>(true) == null)
                    continue;   // покадровое тело — дугу вести нечем

                AnimationClip attack = unit.Visual != null ? unit.Visual.AttackClip : null;
                if (attack == null) continue;   // покрыто Visuals_RequiredBaseSlotsFilled

                if (!ClipMarkers.StrikeWindowNormalized(attack, out _, out _))
                    missing.Add($"{unit.name} → {attack.name}");
            }

            Assert.IsEmpty(missing,
                "Клипы атак скелетных китов без разметки взмаха: " + string.Join("; ", missing) +
                $". Нужны AnimationEvent '{ClipMarkers.StrikeStartFunction}' и " +
                $"'{ClipMarkers.StrikeEndFunction}' на границах фазы удара — без них не будет ни дуги за " +
                "клинком, ни точки, откуда пришёл удар, и молчать об этом дороже, чем падать здесь.");
        }

        /// <summary>
        /// Разметка гвардии — тоже целиком или никак, и по той же причине: показ играет клип щита ТРЕМЯ
        /// кусками по трём разным часам (подъём за время подводки, держание пока живёт барьер, возврат за
        /// своё), и границы кусков берёт из маркеров. Один маркер из двух даёт ровно то же, что и ноль
        /// маркеров, — щит, поднимающийся целым клипом и не опускающийся вовсе.
        /// </summary>
        [Test]
        public void Visuals_GuardWindow_IsWholeOrAbsent()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                AnimationClip guard = vis.GuardClip;
                if (guard == null) continue;   // кит без щита — это отсутствие контента, а не дефект

                bool hasUp   = ClipMarkers.FirstTimeOf(guard, ClipMarkers.GuardUpFunction)   >= 0f;
                bool hasDown = ClipMarkers.FirstTimeOf(guard, ClipMarkers.GuardDownFunction) >= 0f;

                Assert.AreEqual(hasUp, hasDown,
                    $"UnitVisual '{vis.name}': в клипе гвардии '{guard.name}' размечена ПОЛОВИНА жеста " +
                    $"({ClipMarkers.GuardUpFunction}={hasUp}, {ClipMarkers.GuardDownFunction}={hasDown}), " +
                    $"{AssetDatabase.GetAssetPath(vis)}.");

                if (!hasUp) continue;

                Assert.IsTrue(ClipMarkers.GuardWindowNormalized(guard, out float up, out float down),
                    $"UnitVisual '{vis.name}': маркеры гвардии в '{guard.name}' стоят не по порядку — " +
                    $"{ClipMarkers.GuardUpFunction} обязан идти РАНЬШЕ {ClipMarkers.GuardDownFunction}, " +
                    "иначе держать позу нечем.");
                Assert.Greater(up, 0f,
                    $"UnitVisual '{vis.name}': '{ClipMarkers.GuardUpFunction}' стоит в нуле — подъёма щита " +
                    "нет вовсе, поза встаёт мгновенно и телеграф не читается.");
                Assert.Less(down, 1f,
                    $"UnitVisual '{vis.name}': '{ClipMarkers.GuardDownFunction}' стоит в конце клипа — " +
                    "опускать руку нечем, и она растворится в базе вместо возврата.");
            }
        }

        [Test]
        public void Visuals_SkillClipsMarkerWithinClip()
        {
            foreach (UnitVisual vis in AllVisuals())
            {
                string path = AssetDatabase.GetAssetPath(vis);
                for (int slot = 0; slot < 4; slot++)
                {
                    AnimationClip clip = vis.SkillClip(slot);
                    if (clip == null) continue; // слот необязателен

                    float t = ClipMarkers.FirstHitTime(clip);
                    Assert.GreaterOrEqual(t, 0f, $"UnitVisual '{vis.name}' Skill{slot + 1} clip has no \"Marker\" event ({path}).");
                    Assert.LessOrEqual(t, clip.length,
                        $"UnitVisual '{vis.name}' Skill{slot + 1} marker at {t}s past clip end {clip.length}s ({path}).");
                }
            }
        }

        [Test]
        public void AbilityVisualSlots_PointToNonEmptySlot()
        {
            foreach (UnitData unit in ContentIdUtility.FindAll().OfType<UnitData>())
            {
                UnitVisual vis = unit.Visual;
                if (vis == null || unit.Abilities == null) continue; // визуал есть не у всех — тогда слоты неактуальны

                foreach (AbilityData ability in unit.Abilities)
                {
                    if (ability == null) continue;
                    Assert.IsNotNull(vis.SkillClip((int)ability.VisualSlot),
                        $"Unit '{unit.Id}' ability '{ability.Id}' VisualSlot={ability.VisualSlot} points to an empty clip slot " +
                        $"on visual '{vis.name}'.");
                }
            }
        }
    }
}
