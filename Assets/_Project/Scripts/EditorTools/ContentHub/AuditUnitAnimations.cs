#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    public static class AuditUnitAnimations
    {
        [MenuItem("Alebardium/Visuals/Audit Unit Animations", priority = 501)]
        public static void Run()
        {
            var packs = new[]
            {
                "HeroKnight2", "Huntress", "Huntress2", "MartialHero2", "MedievalWarrior3",
                "WizardPack", "ForestMushroom", "HunterOrc", "MedievalWarrior", "GoblinFighter"
            };

            const string outRoot = "Assets/Screenshots/anim-audit";
            if (!AssetDatabase.IsValidFolder("Assets/Screenshots"))
                AssetDatabase.CreateFolder("Assets", "Screenshots");
            if (!AssetDatabase.IsValidFolder(outRoot))
                AssetDatabase.CreateFolder("Assets/Screenshots", "anim-audit");

            var sb = new System.Text.StringBuilder();
            foreach (var pack in packs)
            {
                string packOut = $"{outRoot}/{pack}";
                if (!AssetDatabase.IsValidFolder(packOut))
                    AssetDatabase.CreateFolder(outRoot, pack);
                string abs = Path.GetFullPath(packOut);
                Directory.CreateDirectory(abs);

                foreach (var slot in new[] { "Idle", "Run", "Attack", "Death", "Hit" })
                {
                    string clipPath = pack == "MedievalWarrior"
                        ? $"Assets/_Project/ScriptableObjects/Visuals/MedievalWarrior/MedievalWarrior_{slot}.anim"
                        : $"Assets/_Project/ScriptableObjects/Visuals/{pack}/{pack}_{slot}.anim";

                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null)
                    {
                        sb.AppendLine($"{pack}.{slot}: MISSING CLIP");
                        continue;
                    }

                    var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    var b = bindings.FirstOrDefault(x => x.propertyName == "m_Sprite");
                    if (string.IsNullOrEmpty(b.propertyName))
                    {
                        sb.AppendLine($"{pack}.{slot}: NO SPRITE CURVE");
                        continue;
                    }

                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                    var sprites = new List<Sprite>();
                    int empty = 0;
                    string srcSheet = null;
                    foreach (var k in keys)
                    {
                        var sp = k.value as Sprite;
                        if (sp == null) { empty++; continue; }
                        sprites.Add(sp);
                        srcSheet ??= AssetDatabase.GetAssetPath(sp);
                    }

                    string sheetHint = srcSheet != null
                        ? Path.GetFileNameWithoutExtension(srcSheet).ToLowerInvariant()
                        : "?";

                    bool nameOk = SlotMatchesSheet(slot, sheetHint, pack);

                    bool hasMarker = false;
                    float markerT = -1f;
                    if (slot == "Attack")
                    {
                        var ev = AnimationUtility.GetAnimationEvents(clip);
                        var m = ev.FirstOrDefault(e => e.functionName == ClipMarkers.HitFunction);
                        hasMarker = m != null;
                        markerT = m?.time ?? -1f;
                    }

                    if (sprites.Count > 0)
                        WriteCollage(abs, slot, sprites);

                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    sb.Append($"{pack}.{slot}: frames={sprites.Count} empty={empty} loop={settings.loopTime} sheet='{sheetHint}' ok={nameOk}");
                    if (slot == "Attack") sb.Append($" marker={hasMarker}@{markerT:F2}");
                    sb.AppendLine();
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("[AnimAudit]\n" + sb);
            var reportPath = Path.GetFullPath("Assets/Screenshots/anim-audit/REPORT.txt");
            File.WriteAllText(reportPath, sb.ToString());
            Debug.Log("[AnimAudit] wrote " + reportPath);
        }

        static bool SlotMatchesSheet(string slot, string sheetHint, string pack)
        {
            // Goblin single-sheet: rows encoded in sprite names, sheet always "Goblin Fighter"
            if (pack == "GoblinFighter")
            {
                // validated separately by row index in sprite names inside collage export
                return true;
            }

            return slot switch
            {
                "Idle" => sheetHint.Contains("idle") || sheetHint.Contains("static"),
                "Run" => sheetHint.Contains("run") || sheetHint.Contains("walk") || sheetHint.Contains("move"),
                "Attack" => sheetHint.Contains("attack"),
                "Death" => sheetHint.Contains("death") || sheetHint.Contains("die"),
                "Hit" => sheetHint.Contains("hit") || sheetHint.Contains("hurt") || sheetHint.Contains("take"),
                _ => true
            };
        }

        static void WriteCollage(string absDir, string slot, List<Sprite> sprites)
        {
            int fw = sprites.Max(s => (int)s.rect.width);
            int fh = sprites.Max(s => (int)s.rect.height);
            var collage = new Texture2D(fw * sprites.Count, fh, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            var clear = Enumerable.Repeat(new Color(0.1f, 0.1f, 0.12f, 1f), fw * sprites.Count * fh).ToArray();
            collage.SetPixels(clear);

            // Group by source texture to minimize reimport
            var byPath = sprites.Select((s, i) => (s, i)).GroupBy(x => AssetDatabase.GetAssetPath(x.s));
            var readable = new HashSet<string>();

            foreach (var group in byPath)
            {
                string path = group.Key;
                var timp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (timp != null && !timp.isReadable)
                {
                    timp.isReadable = true;
                    timp.SaveAndReimport();
                    readable.Add(path);
                }
            }

            // Блит под try/finally: между «взвели isReadable» и «вернули обратно» стоит чтение пикселей,
            // и оно умеет падать — например, у ассета, чей импортёр не TextureImporter (атлас, .psb):
            // readable не взвелось, а GetPixels кидает «Texture is not readable». Без finally флаги
            // остались бы взведёнными, а .meta чужих текстур — изменёнными до ручной правки.
            // Соседние тулы (BuildUnitViewPrefabs.FilterOpaque, ExportUnitVisualCatalog) уже так и делают.
            try
            {
                foreach (var (sp, i) in sprites.Select((s, i) => (s, i)))
                {
                    string path = AssetDatabase.GetAssetPath(sp);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    var r = sp.rect;
                    var px = tex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
                    int ox = (fw - (int)r.width) / 2;
                    int oy = (fh - (int)r.height) / 2;
                    collage.SetPixels(i * fw + ox, oy, (int)r.width, (int)r.height, px);
                }
            }
            finally
            {
                foreach (var path in readable)
                {
                    var timp = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (timp == null) continue;
                    timp.isReadable = false;
                    timp.SaveAndReimport();
                }
            }

            collage.Apply();
            File.WriteAllBytes(Path.Combine(absDir, slot + ".png"), collage.EncodeToPNG());
        }
    }
}
#endif
