using System.IO;
using System.Reflection;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Регрессия гейта S1 (вики «12» §5): кросс-сборочный <c>[SerializeReference]</c>. Реальный
    /// Combat-компонент <see cref="PeriodicDamageComponent"/> (реализует Data-маркер
    /// <see cref="IEffectComponent"/>) кладётся в поле <c>EffectData._components</c> (Data),
    /// сохраняется в <c>.asset</c>, форс-реимпортится и читается обратно — тип и данные обязаны
    /// выжить, хотя сборка Data не знает про Combat.
    /// </summary>
    public sealed class SerializeReferenceSpikeTests
    {
        private const string AssetPath = "Assets/Tests/EditMode/__spike_effect.asset";

        [Test]
        public void SerializeReference_CombatComponent_SurvivesReloadInDataAsset()
        {
            try
            {
                var effect = ScriptableObject.CreateInstance<EffectData>();

                // Combat-компонент с заданным значением поля.
                var component = new PeriodicDamageComponent();
                typeof(PeriodicDamageComponent)
                    .GetField("_interval", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(component, 0.42f);

                var so = new SerializedObject(effect);
                SerializedProperty list = so.FindProperty("_components");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).managedReferenceValue = component;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Round-trip через диск: YAML → форс-реимпорт → чтение обратно.
                AssetDatabase.CreateAsset(effect, AssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

                var loaded = AssetDatabase.LoadAssetAtPath<EffectData>(AssetPath);

                Assert.IsNotNull(loaded, "EffectData не загрузился из .asset");
                Assert.IsNotNull(loaded.Components, "_components равен null после reimport");
                Assert.AreEqual(1, loaded.Components.Length, "Потерян элемент массива компонентов");

                var probe = loaded.Components[0] as PeriodicDamageComponent;
                Assert.IsNotNull(probe, "Combat-тип не пережил сериализацию в Data-поле (S1)");
                Assert.AreEqual(0.42f, probe.Interval, 1e-6f, "Данные компонента не сериализовались");
            }
            finally
            {
                if (File.Exists(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            }
        }
    }
}
