using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Контракт размера боевых VFX: <c>VfxData.SizeUnits</c> — единственный владелец, и всё остальное
    /// обязано ему не мешать.
    ///
    /// Тест существует потому, что 31.07.2026 боевая искра оказалась размером в два пикселя при
    /// заявленных в инспекторе 0.09: кривая <c>sizeOverLifetime</c> множила размер на 0.40 в момент
    /// рождения, а сверху лежали ещё два множителя. Ни одно место по отдельности не выглядело
    /// подозрительно — именно такие расхождения ловятся тестом, а не ревью.
    /// </summary>
    public sealed class VfxSizeContractTests
    {
        /// <summary>Высота боевого кадра в мировых единицах: экшн-камера в дуэли держит примерно столько.</summary>
        private const float FrameHeightUnits = 11f;
        private const float ScreenHeightPx = 1080f;
        private const float PixelsPerUnit = ScreenHeightPx / FrameHeightUnits;

        /// <summary>
        /// Ниже этого эффект физически не читается: субпиксельная искра и есть тот случай, из-за которого
        /// боевых VFX «не было видно вообще».
        /// </summary>
        private const float MinVisiblePx = 6f;

        private static IEnumerable<VfxData> AllVfx() =>
            AssetDatabase.FindAssets("t:VfxData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<VfxData>)
                .Where(v => v != null)
                .OrderBy(v => v.Id);

        private static CombatFeelConfigProbe Feel()
        {
            string path = AssetDatabase.FindAssets("t:CombatFeelConfig")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault();
            var asset = path != null ? AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) : null;
            return new CombatFeelConfigProbe(asset);
        }

        /// <summary>Читает множитель размера из feel-конфига, не завязываясь на порядок полей.</summary>
        private sealed class CombatFeelConfigProbe
        {
            private readonly ScriptableObject _asset;
            public CombatFeelConfigProbe(ScriptableObject asset) => _asset = asset;

            public float MinSizeMultiplier
            {
                get
                {
                    if (_asset == null) return 1f;
                    var so = new SerializedObject(_asset);
                    var prop = so.FindProperty("_vfxHitSizeMultMin");
                    return prop != null ? prop.floatValue : 1f;
                }
            }
        }

        [Test]
        public void EveryVfxHasPrefabWithPooledRootAndPositiveSize()
        {
            var problems = new List<string>();
            foreach (VfxData vfx in AllVfx())
            {
                if (vfx.Prefab == null) { problems.Add($"{vfx.Id}: префаб не задан"); continue; }
                if (!vfx.Prefab.TryGetComponent(out PooledVfx _))
                    problems.Add($"{vfx.Id}: на корне префаба нет PooledVfx — CombatVfx откажется его спавнить");
                if (vfx.SizeUnits <= 0f)
                    problems.Add($"{vfx.Id}: SizeUnits = {vfx.SizeUnits}, эффект вырождается в точку");
            }

            Assert.That(problems, Is.Empty, "Нарушен контракт VfxData:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// Кривая жизни размера не имеет права душить РОЖДЕНИЕ частицы. Спад к смерти — законно и нужно;
        /// множитель меньше единицы в нуле делает заявленный <c>startSize</c> недостижимым, и тогда ни
        /// префаб, ни SO не говорят правду о размере.
        /// </summary>
        [Test]
        public void SizeOverLifetimeNeverChokesTheBirthFrame()
        {
            var problems = new List<string>();
            foreach (VfxData vfx in AllVfx())
            {
                if (vfx.Prefab == null) continue;
                foreach (ParticleSystem ps in vfx.Prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
                    if (!sz.enabled) continue;

                    AnimationCurve curve = sz.size.curve;
                    if (curve == null || curve.length == 0) continue;

                    float atBirth = curve.Evaluate(0f) * sz.size.curveMultiplier;
                    // Растущие кривые законны (расширяющееся ядро вспышки) — их пик и есть заявленный размер.
                    // Шагаем целыми: дробный аккумулятор не доходит до t = 1, где у растущей кривой и стоит пик.
                    const int samples = 20;
                    float peak = 0f;
                    for (int s = 0; s <= samples; s++)
                        peak = Mathf.Max(peak, curve.Evaluate(s / (float)samples) * sz.size.curveMultiplier);

                    if (peak < 0.999f)
                        problems.Add($"{vfx.Id}/{ps.name}: кривая размера нигде не достигает 1.0 " +
                                     $"(рождение {atBirth:F2}, пик {peak:F2}) — заявленный startSize недостижим");
                }
            }

            Assert.That(problems, Is.Empty, "Кривая размера душит частицу:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// Корневой масштаб префаба обязан быть единичным, а частицы — масштабироваться по иерархии.
        /// Иначе <c>PooledVfx</c> приводит пропорции к <c>SizeUnits</c>, а трансформ или режим scaling
        /// молча множит результат ещё раз, и число в SO перестаёт значить мировые единицы.
        /// </summary>
        [Test]
        public void PrefabRootScaleIsOneAndParticlesScaleByHierarchy()
        {
            var problems = new List<string>();
            foreach (VfxData vfx in AllVfx())
            {
                if (vfx.Prefab == null) continue;

                Vector3 scale = vfx.Prefab.transform.localScale;
                if ((scale - Vector3.one).sqrMagnitude > 1e-6f)
                    problems.Add($"{vfx.Id}: корневой localScale {scale} вместо (1,1,1) — SizeUnits будет умножен вторично");

                foreach (ParticleSystem ps in vfx.Prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystemScalingMode mode = ps.main.scalingMode;
                    if (mode == ParticleSystemScalingMode.Local)
                        problems.Add($"{vfx.Id}/{ps.name}: scalingMode = Local, размер не поедет за трансформом");
                }
            }

            Assert.That(problems, Is.Empty, "Масштаб префаба врёт о размере:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// Гарантированный минимум частиц у бёрста. Читается ПО РЕЖИМУ кривой: при <c>Constant</c> поле
        /// <c>constantMin</c> не участвует и всегда возвращает ноль, поэтому проверка «min больше нуля»
        /// на таком бёрсте ловит несуществующий дефект. Разброс живёт только в <c>TwoConstants</c>.
        /// </summary>
        private static float GuaranteedCount(ParticleSystem.MinMaxCurve count) =>
            count.mode == ParticleSystemCurveMode.TwoConstants ? count.constantMin : count.constant;

        /// <summary>Бёрст обязан выдавать хотя бы одну частицу — иначе удар может просто не состояться.</summary>
        [Test]
        public void EveryBurstAlwaysEmitsAtLeastOneParticle()
        {
            var problems = new List<string>();
            foreach (VfxData vfx in AllVfx())
            {
                if (vfx.Prefab == null) continue;
                foreach (ParticleSystem ps in vfx.Prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.EmissionModule em = ps.emission;
                    for (int b = 0; b < em.burstCount; b++)
                    {
                        ParticleSystem.Burst burst = em.GetBurst(b);
                        float guaranteed = GuaranteedCount(burst.count);
                        if (guaranteed < 1f)
                            problems.Add($"{vfx.Id}/{ps.name}: бёрст {b} ({burst.count.mode}) " +
                                         $"гарантирует {guaranteed:F0} частиц");
                        if (burst.probability < 1f)
                            problems.Add($"{vfx.Id}/{ps.name}: бёрст {b} играет с вероятностью " +
                                         $"{burst.probability:F2} — эффект случается не всегда");
                    }
                }
            }

            Assert.That(problems, Is.Empty, "Эффект может не состояться:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// Реестр размеров в ПИКСЕЛЯХ боевого кадра — то, чего не было видно ни из префаба, ни из SO.
        /// Считается по худшему случаю: слабый удар, то есть с минимальным множителем из feel-конфига.
        /// </summary>
        [Test]
        public void EveryVfxIsVisibleInTheBattleFrame()
        {
            float minMult = Feel().MinSizeMultiplier;
            var problems = new List<string>();
            var registry = new List<string>();

            foreach (VfxData vfx in AllVfx())
            {
                float weakPx = vfx.SizeUnits * minMult * PixelsPerUnit;
                float fullPx = vfx.SizeUnits * PixelsPerUnit;
                registry.Add($"  {vfx.Id,-18} SizeUnits={vfx.SizeUnits:F3} → {fullPx:F0}px, " +
                             $"слабый удар {weakPx:F0}px");

                if (weakPx < MinVisiblePx)
                    problems.Add($"{vfx.Id}: на слабом ударе {weakPx:F1}px (< {MinVisiblePx}) — не читается");
            }

            TestContext.WriteLine($"Размеры VFX в боевом кадре ({PixelsPerUnit:F0} px на мировую единицу, " +
                                  $"минимальный множитель {minMult:F2}):");
            TestContext.WriteLine(string.Join("\n", registry));

            Assert.That(problems, Is.Empty, "Эффект слишком мелкий для боевого кадра:\n" + string.Join("\n", problems));
        }
    }
}
