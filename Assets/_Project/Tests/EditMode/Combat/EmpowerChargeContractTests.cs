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
    /// самом эффекте-носителе: иначе удар потратит множитель, а эффект останется висеть на бойце до
    /// конца боя.
    /// <para>Почему тестом, а не комментарием: шов проходит между ассетом (теги эффекта), компонентом
    /// (<c>_consumeTag</c>) и <c>AutoAttackSystem</c> (диспел по тегу). Ни одна из трёх сторон не видит
    /// двух других.</para>
    /// <para><b>Чем это стоит,</b> раз усиление всё же тратится: висящий израсходованный заряд врёт
    /// показу («удар взведён»), собирает на себя чужой purge вместо настоящего бафа и подсовывает
    /// неверный ответ каждому, кто спросит «есть ли заряд» по списку эффектов, а не по множителю.
    /// Так и было до 2026-08-01 у трёх вражеских зарядов — цикла голема и сферы гоблина-мага.</para>
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
            var broken = new List<string>();

            foreach (EffectData effect in AllEffects())
            {
                if (effect.Components == null) continue;

                foreach (object component in effect.Components)
                {
                    if (component is not EmpowerNextAttackComponent empower) continue;

                    checked_++;
                    EffectTag consumeTag = Field<EffectTag>(empower, "_consumeTag");

                    // Нарушители копятся, а не роняют прогон на первом: три заряда с одной и той же дырой
                    // выглядели как один дефект GolemSlam, и починка «одного» ничего бы не изменила.
                    if (consumeTag == EffectTag.None)
                    {
                        broken.Add($"{effect.name}: заряд усиления без тега снятия — удар потратит " +
                                   "множитель, а сам заряд останется висеть на бойце до конца боя.");
                        continue;
                    }

                    if (!effect.Tags.HasFlag(consumeTag))
                        broken.Add($"{effect.name}: компонент снимается по тегу {consumeTag}, но у эффекта " +
                                   $"теги {effect.Tags} — снимать удару будет нечего.");

                    if (effect.Unremovable)
                        broken.Add($"{effect.name}: заряд помечен неснимаемым, а тратится он именно " +
                                   "снятием — усиление никогда не израсходуется.");
                }
            }

            Assert.Greater(checked_, 0, "Ни одного заряда усиления не найдено — тест потерял предмет проверки.");
            Assert.IsEmpty(broken, "Заряды усиления с дырой в контракте:\n" + string.Join("\n", broken));
        }
    }
}
