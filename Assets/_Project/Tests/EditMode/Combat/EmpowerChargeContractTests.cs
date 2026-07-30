using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Контракт заряда усиленной авто-атаки. Заряд взводит компонент, а СНИМАЕТ его удар — по тегу,
    /// который тот же компонент записал в юнита. Значит тег, объявленный компонентом, обязан быть на
    /// самом эффекте-носителе: иначе удар применит множитель, но эффект останется висеть, и кит будет
    /// бить усиленно всегда.
    /// <para>Почему тестом, а не комментарием: шов проходит между ассетом (теги эффекта), компонентом
    /// (<c>_consumeTag</c>) и <c>AutoAttackSystem</c> (диспел по тегу). Ни одна из трёх сторон не видит
    /// двух других, а цена расхождения — бесконечное усиление, которое в бою читается как «кит бьёт
    /// вдвое, но иногда».</para>
    /// </summary>
    public sealed class EmpowerChargeContractTests
    {
        private static IEnumerable<EffectData> AllEffects() =>
            AssetDatabase.FindAssets($"t:{nameof(EffectData)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EffectData>)
                .Where(e => e != null)
                .OrderBy(e => e.name);

        private static T Field<T>(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"{target.GetType().Name}: нет поля {name} — тест отстал от кода.");
            return (T)f.GetValue(target);
        }

        [Test]
        public void EmpowerCharge_CarriesTheTagItIsConsumedBy()
        {
            var checked_ = 0;

            foreach (EffectData effect in AllEffects())
            {
                if (effect.Components == null) continue;

                foreach (object component in effect.Components)
                {
                    if (component is not EmpowerNextAttackComponent empower) continue;

                    EffectTag consumeTag = Field<EffectTag>(empower, "_consumeTag");
                    Assert.AreNotEqual(EffectTag.None, consumeTag,
                        $"{effect.name}: заряд усиления без тега снятия — удар не сможет его убрать, " +
                        "и усиление станет постоянным.");

                    Assert.IsTrue(effect.Tags.HasFlag(consumeTag),
                        $"{effect.name}: компонент снимается по тегу {consumeTag}, но у эффекта теги " +
                        $"{effect.Tags} — удар усилит атаку и оставит заряд висеть навсегда.");

                    Assert.IsFalse(effect.Unremovable,
                        $"{effect.name}: заряд помечен неснимаемым, а тратится он именно снятием — " +
                        "усиление никогда не израсходуется.");

                    checked_++;
                }
            }

            Assert.Greater(checked_, 0, "Ни одного заряда усиления не найдено — тест потерял предмет проверки.");
        }
    }
}
