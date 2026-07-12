using System.Linq;
using Guildmaster.ContentHub.Editor;
using Guildmaster.Data.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.ContentHub
{
    public sealed class ConfigDiffTests
    {
        [Test]
        public void FreshInstance_HasNoDiff()
        {
            var cfg = ScriptableObject.CreateInstance<StatsConfig>();
            try { Assert.IsEmpty(ConfigDiff.Diff(cfg)); }
            finally { Object.DestroyImmediate(cfg); }
        }

        [Test]
        public void ChangedField_ShowsInDiff()
        {
            var cfg = ScriptableObject.CreateInstance<StatsConfig>();
            try
            {
                using (var so = new SerializedObject(cfg))
                {
                    var p = so.FindProperty("_armorConstantK");
                    Assert.IsNotNull(p, "поле _armorConstantK должно существовать");
                    p.floatValue = 250f;
                    so.ApplyModifiedProperties();
                }

                var diff = ConfigDiff.Diff(cfg);
                Assert.IsTrue(diff.Any(r => r.Path.Contains("armorConstantK")),
                    "изменённое поле должно попасть в diff");
            }
            finally { Object.DestroyImmediate(cfg); }
        }
    }
}
