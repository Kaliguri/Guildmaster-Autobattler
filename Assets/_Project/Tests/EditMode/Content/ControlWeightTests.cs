using System.Collections.Generic;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;

namespace Guildmaster.Tests.EditMode.Content
{
    /// <summary>
    /// Вес контроля (<see cref="EffectData.ControlWeight"/>): цена секунды эффекта для стенда.
    /// </summary>
    /// <remarks>
    /// Инвариант живёт в тесте, а не в комментарии, потому что нарушается он в ДРУГОМ файле и молча:
    /// заводят новый контроль-эффект, вес оставляют нулевым — и кит, держащий врага полбоя, просто не
    /// попадает в счёт контроля. Ноль в отчёте неотличим от честно измеренного нуля; ровно этой тишиной
    /// три недели скрывались целители в замерах и убийцы в бенче замены (21.08.2026).
    /// <para>Шкала — решение Макса 2026-08-22 (<c>gdd/00-meta/journal-adr</c> §2026-08-22 (2)):
    /// 1.0 полный запрет, 0.7 корень, 0.5 частичный, 0.166 замедление.</para>
    /// </remarks>
    public sealed class ControlWeightTests
    {
        private static List<EffectData> AllEffects()
        {
            var effects = new List<EffectData>();
            foreach (string guid in AssetDatabase.FindAssets("t:EffectData"))
            {
                var effect = AssetDatabase.LoadAssetAtPath<EffectData>(AssetDatabase.GUIDToAssetPath(guid));
                if (effect != null) effects.Add(effect);
            }
            return effects;
        }

        /// <summary>Эффект, запрещающий действия, обязан объявить цену своей секунды.</summary>
        [Test]
        public void EveryEffect_ThatPreventsAction_HasControlWeight()
        {
            foreach (EffectData effect in AllEffects())
            {
                if (effect.Components == null) continue;

                bool prevents = false;
                foreach (IEffectComponent component in effect.Components)
                {
                    if (component is not ControlComponent control) continue;
                    if (control.PreventAct || control.PreventMove || control.PreventCast) prevents = true;
                }

                if (!prevents) continue;

                Assert.Greater(effect.ControlWeight, 0f,
                    $"«{effect.name}» запрещает действия, но вес контроля нулевой " +
                    $"({AssetDatabase.GetAssetPath(effect)}). Стенд не увидит его в счёте вовсе — " +
                    "шкала: 1.0 полный запрет, 0.7 корень, 0.5 частичный, 0.166 замедление.");
            }
        }

        /// <summary>
        /// Верхняя ступень шкалы — единица: секунда контроля не может стоить больше секунды.
        /// </summary>
        [Test]
        public void NoEffect_CostsMoreThanASecondPerSecond()
        {
            foreach (EffectData effect in AllEffects())
            {
                Assert.LessOrEqual(effect.ControlWeight, 1f,
                    $"«{effect.name}» весит {effect.ControlWeight} — больше единицы " +
                    $"({AssetDatabase.GetAssetPath(effect)}). Единица означает «цель выключена целиком»; " +
                    "выше неё шкала не растёт, иначе счёт перестаёт сравниваться между китами.");

                Assert.GreaterOrEqual(effect.ControlWeight, 0f,
                    $"«{effect.name}» весит отрицательно ({AssetDatabase.GetAssetPath(effect)})");
            }
        }
    }
}
