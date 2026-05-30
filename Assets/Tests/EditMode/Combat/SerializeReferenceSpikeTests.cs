using System.IO;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Combat
{
    /// <summary>
    /// Спайк S1 (вики «12» §5) — ГЕЙТ Фазы 2. Доказывает кросс-сборочный <c>[SerializeReference]</c>:
    /// Combat-тип <see cref="SpikeProbeComponent"/> (реализует Data-маркер
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

                // Назначаем Combat-тип в [SerializeReference]-элемент через настоящий editor-API.
                var so = new SerializedObject(effect);
                SerializedProperty list = so.FindProperty("_components");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).managedReferenceValue =
                    new SpikeProbeComponent { Marker = 4242 };
                so.ApplyModifiedPropertiesWithoutUndo();

                // Round-trip через диск: запись YAML → форс-реимпорт → чтение обратно.
                AssetDatabase.CreateAsset(effect, AssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

                var loaded = AssetDatabase.LoadAssetAtPath<EffectData>(AssetPath);

                Assert.IsNotNull(loaded, "EffectData не загрузился из .asset");
                Assert.IsNotNull(loaded.Components, "_components равен null после reimport");
                Assert.AreEqual(1, loaded.Components.Length, "Потерян элемент массива компонентов");

                var probe = loaded.Components[0] as SpikeProbeComponent;
                Assert.IsNotNull(probe, "Combat-тип не пережил сериализацию в Data-поле (S1 провалился)");
                Assert.AreEqual(4242, probe.Marker, "Данные компонента не сериализовались");
            }
            finally
            {
                if (File.Exists(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            }
        }
    }
}
