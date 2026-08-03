using Guildmaster.Presentation.Design;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Форма кривой «вес удара → размер боевой цифры». Числа (`NumberMaxScale`, `NumberFullFrac`) —
    /// дизайнерские и живут в ассете конфига, тест их не трогает; он держит ФОРМУ: рост монотонный,
    /// насыщается на пороге и в нижней трети шкалы идёт быстрее прямой. Форму легко потерять при
    /// правке «просто верну Lerp» — и тогда все обычные удары снова станут одного мелкого размера.
    /// </summary>
    public sealed class CombatNumberScaleTests
    {
        private static CombatFeelConfig Config() => ScriptableObject.CreateInstance<CombatFeelConfig>();

        [Test]
        public void Царапина_в_свой_размер_а_порог_и_выше_в_максимум()
        {
            CombatFeelConfig feel = Config();
            try
            {
                Assert.That(feel.EvaluateNumberScale(0f), Is.EqualTo(1f).Within(1e-4f), "нулевой урон уже увеличил цифру");

                float atFull = feel.EvaluateNumberScale(feel.NumberFullFrac);
                Assert.That(feel.EvaluateNumberScale(feel.NumberFullFrac * 4f), Is.EqualTo(atFull).Within(1e-4f),
                            "за порогом цифра продолжила расти — кит будет заслонять экран");
            }
            finally { Object.DestroyImmediate(feel); }
        }

        [Test]
        public void Рост_монотонный()
        {
            CombatFeelConfig feel = Config();
            try
            {
                float prev = 0f;
                for (float frac = 0f; frac <= 0.5f; frac += 0.01f)
                {
                    float scale = feel.EvaluateNumberScale(frac);
                    Assert.That(scale, Is.GreaterThanOrEqualTo(prev - 1e-5f), $"на доле {frac:F2} цифра уменьшилась");
                    prev = scale;
                }
            }
            finally { Object.DestroyImmediate(feel); }
        }

        [Test]
        public void В_нижней_трети_шкалы_кривая_обгоняет_прямую()
        {
            // Смысл корня: половина всего прироста выдаётся уже на четверти порога. На прямой там была бы
            // четверть — обычные удары не отличались бы друг от друга, и размер работал бы только для кита.
            CombatFeelConfig feel = Config();
            try
            {
                float quarter = feel.EvaluateNumberScale(feel.NumberFullFrac * 0.25f) - 1f;
                float full    = feel.EvaluateNumberScale(feel.NumberFullFrac)         - 1f;

                Assert.That(full, Is.GreaterThan(0f), "конфиг без запаса размера — тест бессмыслен");
                Assert.That(quarter / full, Is.EqualTo(0.5f).Within(0.02f),
                            "кривая больше не корневая: на четверти порога должна быть половина прироста");
            }
            finally { Object.DestroyImmediate(feel); }
        }
    }
}
