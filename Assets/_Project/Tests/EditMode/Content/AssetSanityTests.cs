using System.Collections.Generic;
using System.Linq;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Ловит поля ассетов, которые формально валидны, а игру ломают: enum держит чужое значение,
    /// стат забыт и молча равен нулю. Общее у всех случаев одно — тесты зелёные, а в бою тишина.
    /// <para><b>Зачем гейт.</b> Ассеты часто заводятся кодом, а `SerializedProperty.enumValueIndex`
    /// присваивает позицию в списке имён, не значение enum; совпадают они только у перечислений без
    /// дыр. 21.08.2026 так уехали пять ассетов сразу: автоатака поддержки стала `Pure` (урон, не
    /// гасимый ничем), два бафа легли дебафами, стакинг сменился на соседний. Ни компилятор, ни
    /// остальные тесты контента этого не увидели — поля валидны, просто чужие
    /// (журнал `2026-08-21-enum-value-index-is-a-position-not-a-value`).</para>
    /// <para>Проверки намеренно узкие: ловится не «странное число», а <b>сочетание, которого не
    /// бывает по дизайну</b>. Легальное исключение здесь — повод обсудить дизайн, а не ослабить тест.</para>
    /// </summary>
    public sealed class AssetSanityTests
    {
        /// <summary>Компоненты, которые бывают только у ПОЛЕЗНОГО эффекта: щиты, негейты, лечение.</summary>
        private static readonly HashSet<string> BuffOnly = new HashSet<string>
        {
            nameof(ShieldComponent), nameof(SchoolShieldComponent), nameof(RangedWardComponent),
            nameof(EvasionComponent), nameof(DodgeComponent), nameof(BlockComponent),
            nameof(ParryComponent), nameof(MissingHpShieldComponent), nameof(PeriodicHealComponent),
            nameof(LifestealComponent),
        };

        /// <summary>Компоненты, которые бывают только у ВРАЖДЕБНОГО эффекта: слепота, контроль, DoT.</summary>
        private static readonly HashSet<string> DebuffOnly = new HashSet<string>
        {
            nameof(BlindComponent), nameof(ControlComponent), nameof(SleepComponent),
            nameof(PeriodicDamageComponent),
        };

        /// <summary>
        /// Автоатака не бывает `Pure` и не бывает `Undefined`. Первое обходит броню целиком — это
        /// привилегия редких способностей, а не того, чем боец машет каждую секунду; второе означает
        /// «автор забыл» по докстрингу самого типа.
        /// </summary>
        [Test]
        public void AutoAttackDamageType_IsNeitherPureNorUndefined()
        {
            var wrong = Units()
                .Where(u => u.AutoAttackDamageType == DamageType.Pure
                         || u.AutoAttackDamageType == DamageType.Undefined)
                .Select(u => $"{u.Id}: автоатака {u.AutoAttackDamageType}")
                .ToList();

            Assert.That(wrong, Is.Empty,
                "Автоатака с чистым или незаданным уроном:\n  " + string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Полярность и содержимое не расходятся. Полярность решает, кто снимет эффект: свои снимают
        /// с себя дебафы клинсом, чужие снимают бафы диспелом. Щит, лежащий дебафом, союзники будут
        /// счищать друг с друга, а враг не тронет вовсе — и заметить это можно только в бою.
        /// </summary>
        [Test]
        public void EffectPolarity_MatchesWhatTheEffectActuallyDoes()
        {
            var wrong = new List<string>();

            foreach (EffectData effect in Effects())
            {
                if (effect.Components == null) continue;

                foreach (IEffectComponent component in effect.Components)
                {
                    if (component == null) continue;
                    string name = component.GetType().Name;

                    if (BuffOnly.Contains(name) && effect.Polarity == EffectPolarity.Debuff)
                        wrong.Add($"{effect.Id}: {name} — полезный компонент, а полярность Debuff");

                    if (DebuffOnly.Contains(name) && effect.Polarity == EffectPolarity.Buff)
                        wrong.Add($"{effect.Id}: {name} — враждебный компонент, а полярность Buff");
                }
            }

            Assert.That(wrong, Is.Empty,
                "Полярность разошлась с содержимым эффекта:\n  " + string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Эффект, который КОПИТ стаки, не может быть упёрт в один: правило и число сказали бы
        /// противоположное. `Stack` и `StackAndRefresh` стоят в перечислении рядом и различаются только
        /// тем, обновляется ли срок, — перепутать их числом легко, и `MaxStacks = 1` тогда единственный
        /// видимый след.
        /// <para><b>`Portions` исключено намеренно:</b> у кровотечения порции живут без потолка вовсе
        /// (решение Макса 2026-07-31), и `MaxStacks = 0` там значит «предела нет», а не «поле забыли».</para>
        /// <para><b>Обратной проверки нет тоже намеренно.</b> Потолок у нестакающегося эффекта просто не
        /// читается — оставшаяся в поле двойка безвредна, и тест на неё ловил бы мусор, а не подмену.</para>
        /// </summary>
        [Test]
        public void StackingEffect_DoesNotCapItselfAtOne()
        {
            var wrong = Effects()
                .Where(e => e.Stacking == StackRule.Stack || e.Stacking == StackRule.StackAndRefresh)
                .Where(e => e.MaxStacks <= 1)
                .Select(e => $"{e.Id}: правило {e.Stacking}, а потолок стаков {e.MaxStacks}")
                .ToList();

            Assert.That(wrong, Is.Empty,
                "Эффект копит стаки, но упёрт в один:\n  " + string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Дальнобойный юнит обязан иметь ненулевую скорость снаряда: его автоатака не бьёт напрямую, а
        /// выпускает снаряд, и при `ProjectileSpeed = 0` тот никуда не летит. Юнит исправно замахивается
        /// и не наносит **ровно ноль** урона за весь бой.
        /// <para>Стат забывается легко: остальные приходят каскадом от класса, а этот задаётся только
        /// своей строкой в стат-блоке. 21.08.2026 так простоял весь бой `relic.windwarden` — дефект нашёлся
        /// на балансном стенде по нулю в DmgDealt, и это единственный след, который он оставляет.</para>
        /// </summary>
        [Test]
        public void RangedUnit_HasProjectileSpeed()
        {
            var wrong = Units()
                .Where(u => u.AttackType != AttackType.Melee)
                .Where(u => !u.Stats.Any(m => m.Stat == StatType.ProjectileSpeed && m.Value > 0f))
                .Select(u => $"{u.Id}: {u.AttackType}, а ProjectileSpeed не задан")
                .ToList();

            Assert.That(wrong, Is.Empty,
                "Дальнобойный юнит со снарядом нулевой скорости — он не попадёт ни разу:\n  "
                + string.Join("\n  ", wrong));
        }

        private static IEnumerable<UnitData> Units()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UnitData",
                     new[] { "Assets/_Project/ScriptableObjects" }))
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid));
                if (unit != null) yield return unit;
            }
        }

        private static IEnumerable<EffectData> Effects()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:EffectData",
                     new[] { "Assets/_Project/ScriptableObjects" }))
            {
                var effect = AssetDatabase.LoadAssetAtPath<EffectData>(AssetDatabase.GUIDToAssetPath(guid));
                if (effect != null) yield return effect;
            }
        }
    }
}
