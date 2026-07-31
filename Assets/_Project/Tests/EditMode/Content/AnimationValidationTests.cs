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

                float t = ClipMarkers.FirstMarkerTime(attack);
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
                if (ClipMarkers.FirstMarkerTime(attack) < 0f) continue;   // отсутствие покрыто тестом выше

                Assert.Greater(vis.AttackHitFrame, 0,
                    $"UnitVisual '{vis.name}': маркер контакта стоит на кадре 0 " +
                    $"({AssetDatabase.GetAssetPath(vis)}). Сим выведет из него нулевой замах — удар без " +
                    "подводки. Поставь маркер туда, где оружие реально достаёт цель.");
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

                    float t = ClipMarkers.FirstMarkerTime(clip);
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
