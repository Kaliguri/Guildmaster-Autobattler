#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Одноразовый/повторяемый пайплайн: нарезка листов → клипы → OverrideController →
    /// UnitVisual → Prefab Variant → проводка в Relic/Enemy SO (трек п.0b).
    /// </summary>
    public static class BuildUnitViewPrefabs
    {
        const string ParentPrefab = "Assets/_Project/Prefabs/UnitView.prefab";
        const string UnitsFolder = "Assets/_Project/Prefabs/Units";
        const string VisualsRoot = "Assets/_Project/ScriptableObjects/Visuals";
        const string BaseController = "Assets/_Project/ScriptableObjects/Visuals/UnitBase.controller";
        const float SampleRate = 10f;

        [MenuItem("Alebardium/Visuals/Build Per-Unit View Prefabs", priority = 500)]
        public static void BuildAll()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Units");
            EnsureFolder(VisualsRoot, null);

            // --- Unique art packs ---
            var packs = new[]
            {
                Pack.Square("HeroKnight2",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/Hero Knight 2/Hero Knight 2/Sprites",
                    idle: "Idle.png", run: "Run.png", attack: "Attack.png", death: "Death.png", hit: "Take Hit.png"),
                Pack.Square("Huntress",
                    "Assets/_Project/Art/Sprites/Pixel Art Heroes/Huntress/Sprites",
                    idle: "Idle.png", run: "Run.png", attack: "Attack1.png", death: "Death.png", hit: "Take hit.png"),
                Pack.Square("Huntress2",
                    "Assets/_Project/Art/Sprites/Pixel Art Heroes/Huntress 2/Sprites/Character",
                    idle: "Idle.png", run: "Run.png", attack: "Attack.png", death: "Death.png", hit: "Get Hit.png"),
                Pack.Square("MartialHero2",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/Martial Hero 2/Martial Hero 2/Sprites",
                    idle: "Idle.png", run: "Run.png", attack: "Attack1.png", death: "Death.png", hit: "Take hit.png"),
                Pack.Square("MedievalWarrior3",
                    "Assets/_Project/Art/Sprites/Pixel Art Heroes/Medieval Warrior Pack 3/Sprites",
                    idle: "Idle.png", run: "Run.png", attack: "Attack1.png", death: "Death.png", hit: "Get Hit.png"),
                Pack.FixedCell("WizardPack",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/Wizard Pack/Wizard Pack",
                    cellW: 231, cellH: 190,
                    idle: "Idle.png", run: "Run.png", attack: "Attack1.png", death: "Death.png", hit: "Hit.png"),
                Pack.FixedCell("ForestMushroom",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/Forest_Monsters_FREE/Forest_Monsters_FREE/Mushroom/Mushroom without VFX",
                    cellW: 80, cellH: 64,
                    idle: "Mushroom-Idle.png", run: "Mushroom-Run.png", attack: "Mushroom-Attack.png",
                    death: "Mushroom-Die.png", hit: "Mushroom-Hit.png"),
                Pack.Square("HunterOrc",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/HunterOrc",
                    idle: "Idle/Idle.png", run: "Walk/Walk.png", attack: "Attack1/Attack1.png",
                    death: "Death/Death.png", hit: "Hurt/Hurt.png"),
                Pack.GridRows("GoblinFighter",
                    "Assets/_Project/Art/Sprites/New FREE Pixel Art Heroes/Goblin_Fighter/Goblin Figther Sprites/Goblin Fighter.png",
                    cols: 8, rows: 5, cell: 128,
                    // top→bottom (визуально сверено): Attack, Death, Hit, Idle, Run
                    idleRow: 3, runRow: 4, attackRow: 0, deathRow: 1, hitRow: 2),
            };

            var built = new Dictionary<string, BuiltVisual>(StringComparer.Ordinal);
            foreach (var pack in packs)
            {
                try
                {
                    built[pack.Name] = BuildPack(pack);
                    Debug.Log($"[BuildUnitViews] OK pack {pack.Name}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BuildUnitViews] FAIL pack {pack.Name}: {ex}");
                }
            }

            // Reuse existing MedievalWarrior clips/controller for MW2-based units.
            built["MedievalWarrior"] = BuildFromExistingMedievalWarrior();

            // Prefabs + SO wiring
            Wire("Assets/_Project/ScriptableObjects/Relics/WhirlMonk.asset", "MedievalWarrior", "UnitView_WhirlMonk", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/BaseRelic.asset", "MedievalWarrior", "UnitView_BaseRelic", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/Defender.asset", "HeroKnight2", "UnitView_Defender", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/IronSpearman.asset", "Huntress", "UnitView_IronSpearman", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/Ranger.asset", "Huntress2", "UnitView_Ranger", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/Assassin.asset", "MartialHero2", "UnitView_Assassin", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/FlameSwordsman.asset", "MedievalWarrior3", "UnitView_FlameSwordsman", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Relics/Treant.asset", "MedievalWarrior3", "UnitView_Treant",
                new Color(0.55f, 0.85f, 0.45f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Relics/LightShepherd.asset", "WizardPack", "UnitView_LightShepherd",
                new Color(1f, 0.95f, 0.75f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Relics/Cryomancer.asset", "WizardPack", "UnitView_Cryomancer",
                new Color(0.55f, 0.8f, 1f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Relics/Druid.asset", "MedievalWarrior", "UnitView_Druid",
                new Color(0.43f, 0.72f, 0.42f, 1f));

            Wire("Assets/_Project/ScriptableObjects/Enemies/GoblinGrunt.asset", "GoblinFighter", "UnitView_GoblinGrunt",
                new Color(0.7f, 1f, 0.55f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Enemies/GoblinWarrior.asset", "GoblinFighter", "UnitView_GoblinWarrior",
                new Color(0.55f, 0.85f, 0.4f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Enemies/GoblinCutthroat.asset", "GoblinFighter", "UnitView_GoblinCutthroat",
                new Color(0.9f, 0.75f, 0.4f, 1f));
            Wire("Assets/_Project/ScriptableObjects/Enemies/GoblinArcher.asset", "HunterOrc", "UnitView_GoblinArcher", Color.white);
            Wire("Assets/_Project/ScriptableObjects/Enemies/TrainingDummy.asset", "MedievalWarrior", "UnitView_TrainingDummy",
                new Color(0.65f, 0.65f, 0.7f, 1f));

            void Wire(string soPath, string packName, string prefabName, Color tint)
            {
                if (!built.TryGetValue(packName, out var vis))
                {
                    Debug.LogError($"[BuildUnitViews] Missing built pack '{packName}' for {soPath}");
                    return;
                }

                string prefabPath = $"{UnitsFolder}/{prefabName}.prefab";
                CreateVariantPrefab(prefabPath, vis.OverrideController, vis.IdleSprite);
                AssignToUnitData(soPath, vis.Visual, prefabPath, tint);
                Debug.Log($"[BuildUnitViews] Wired {soPath} → {prefabName} ({packName})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildUnitViews] DONE");
        }

        static BuiltVisual BuildFromExistingMedievalWarrior()
        {
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{VisualsRoot}/MedievalWarrior/MedievalWarrior_Idle.anim");
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{VisualsRoot}/MedievalWarrior/MedievalWarrior_Run.anim");
            var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{VisualsRoot}/MedievalWarrior/MedievalWarrior_Attack.anim");
            var death = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{VisualsRoot}/MedievalWarrior/MedievalWarrior_Death.anim");
            var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{VisualsRoot}/MedievalWarrior/MedievalWarrior_Hit.anim");
            var visual = AssetDatabase.LoadAssetAtPath<UnitVisual>($"{VisualsRoot}/MedievalWarrior.asset");

            // Idle sprite from first key of idle clip
            Sprite idleSprite = FirstSprite(idle);
            var oc = GetOrCreateOverride("MedievalWarrior", idle, run, attack, death, hit);
            return new BuiltVisual(visual, oc, idleSprite);
        }

        static BuiltVisual BuildPack(Pack pack)
        {
            string folder = $"{VisualsRoot}/{pack.Name}";
            EnsureFolder(VisualsRoot, pack.Name);

            SlicePackSheets(pack);

            var idleSp = LoadSortedSprites(pack.Resolve(pack.Idle));
            var runSp = LoadSortedSprites(pack.Resolve(pack.Run));
            var atkSp = LoadSortedSprites(pack.Resolve(pack.Attack));
            var deathSp = LoadSortedSprites(pack.Resolve(pack.Death));
            var hitSp = LoadSortedSprites(pack.Resolve(pack.Hit));

            if (pack.Kind == PackKind.GridRows)
            {
                idleSp = FilterOpaque(pack.RowSprites(pack.IdleRow), pack.SheetPath);
                runSp = FilterOpaque(pack.RowSprites(pack.RunRow), pack.SheetPath);
                atkSp = FilterOpaque(pack.RowSprites(pack.AttackRow), pack.SheetPath);
                deathSp = FilterOpaque(pack.RowSprites(pack.DeathRow), pack.SheetPath);
                hitSp = FilterOpaque(pack.RowSprites(pack.HitRow), pack.SheetPath);
            }

            if (idleSp.Length == 0 || atkSp.Length == 0)
                throw new InvalidOperationException($"No sprites for {pack.Name} idle={idleSp.Length} atk={atkSp.Length}");

            var idle = CreateClip($"{folder}/{pack.Name}_Idle.anim", $"{pack.Name}_Idle", idleSp, loop: true, marker: false);
            var run = CreateClip($"{folder}/{pack.Name}_Run.anim", $"{pack.Name}_Run", runSp.Length > 0 ? runSp : idleSp, loop: true, marker: false);
            var attack = CreateClip($"{folder}/{pack.Name}_Attack.anim", $"{pack.Name}_Attack", atkSp, loop: false, marker: true);
            var death = CreateClip($"{folder}/{pack.Name}_Death.anim", $"{pack.Name}_Death", deathSp.Length > 0 ? deathSp : idleSp, loop: false, marker: false);
            var hit = CreateClip($"{folder}/{pack.Name}_Hit.anim", $"{pack.Name}_Hit", hitSp.Length > 0 ? hitSp : idleSp.Take(1).ToArray(), loop: false, marker: false);

            var visual = CreateUnitVisual($"{folder}/{pack.Name}.asset", idle, run, attack, death, hit, idleSp[0]);
            var oc = GetOrCreateOverride(pack.Name, idle, run, attack, death, hit);
            return new BuiltVisual(visual, oc, idleSp[0]);
        }

        static void SlicePackSheets(Pack pack)
        {
            if (pack.Kind == PackKind.GridRows)
            {
                SliceGrid(pack.SheetPath, pack.Cols, pack.Rows, pack.Cell, pack.Cell);
                pack.CacheGridSprites();
                return;
            }

            foreach (var rel in new[] { pack.Idle, pack.Run, pack.Attack, pack.Death, pack.Hit })
            {
                string path = pack.Resolve(rel);
                if (string.IsNullOrEmpty(path) || !File.Exists(ToAbsolute(path))) continue;
                if (pack.Kind == PackKind.Square)
                    SliceSquare(path);
                else
                    SliceFixed(path, pack.CellW, pack.CellH);
            }
        }

        static void SliceSquare(string assetPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return;
            int h = tex.height;
            if (h <= 0 || tex.width % h != 0)
            {
                Debug.LogWarning($"[BuildUnitViews] Skip square slice (non-divisible): {assetPath} {tex.width}x{h}");
                return;
            }

            SliceGrid(assetPath, tex.width / h, 1, h, h);
        }

        static void SliceFixed(string assetPath, int cellW, int cellH)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return;
            if (cellW <= 0 || cellH <= 0 || tex.width % cellW != 0 || tex.height % cellH != 0)
            {
                Debug.LogWarning($"[BuildUnitViews] Skip fixed slice: {assetPath} {tex.width}x{tex.height} cell {cellW}x{cellH}");
                return;
            }

            SliceGrid(assetPath, tex.width / cellW, tex.height / cellH, cellW, cellH);
        }

        static void SliceGrid(string assetPath, int cols, int rows, int cellW, int cellH)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.spritePixelsPerUnit = 100;

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            var rects = new List<SpriteRect>();
            for (int row = 0; row < rows; row++)
            {
                // Unity rect y=0 is bottom of texture
                int y = (rows - 1 - row) * cellH;
                for (int col = 0; col < cols; col++)
                {
                    string name = rows > 1 ? $"{baseName}_r{row}_{col}" : $"{baseName}_{col}";
                    rects.Add(new SpriteRect
                    {
                        name      = name,
                        rect      = new Rect(col * cellW, y, cellW, cellH),
                        alignment = SpriteAlignment.Center,
                        pivot     = new Vector2(0.5f, 0.5f),
                        // GUID обязателен: по нему провайдер сопоставляет спрайт с уже нарезанным.
                        // Выводим из имени, чтобы повторная нарезка того же листа не плодила дубли.
                        spriteID  = GUID.Generate(),
                    });
                }
            }

            // Нарезка идёт через ISpriteEditorDataProvider: TextureImporter.spritesheet Unity сняла,
            // и это не косметика — старое свойство больше не пишет метаданные (2D-пакет владеет ими сам).
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects.ToArray());
            provider.Apply();

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        static Sprite[] LoadSortedSprites(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return Array.Empty<Sprite>();
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.rect.yMax) // bottom row first for multi; for single row y same
                .ThenBy(s => s.rect.x)
                .ToArray();
        }

        /// <summary>Убирает пустые ячейки сетки (иначе Idle/Attack мигает в прозрачность).</summary>
        static Sprite[] FilterOpaque(Sprite[] sprites, string sheetPath)
        {
            if (sprites == null || sprites.Length == 0) return Array.Empty<Sprite>();
            var imp = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (imp == null) return sprites;
            bool was = imp.isReadable;
            if (!was)
            {
                imp.isReadable = true;
                imp.SaveAndReimport();
            }

            try
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
                if (tex == null) return sprites;
                return sprites.Where(s =>
                {
                    var r = s.rect;
                    var px = tex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
                    return px.Any(c => c.a > 0.1f);
                }).ToArray();
            }
            finally
            {
                if (!was)
                {
                    imp.isReadable = false;
                    imp.SaveAndReimport();
                }
            }
        }

        static AnimationClip CreateClip(string path, string clipName, Sprite[] sprites, bool loop, bool marker)
        {
            if (sprites == null || sprites.Length == 0)
                throw new ArgumentException($"No sprites for clip {clipName}");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = clipName };
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.name = clipName;
            clip.frameRate = SampleRate;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite",
            };
            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                keys[i] = new ObjectReferenceKeyframe
                {
                    time = i / SampleRate,
                    value = sprites[i],
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (marker)
            {
                float t = (Mathf.Max(0, sprites.Length / 2)) / SampleRate;
                AnimationUtility.SetAnimationEvents(clip, new[]
                {
                    new AnimationEvent { functionName = "Marker", time = t },
                });
            }
            else
            {
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            }

            EditorUtility.SetDirty(clip);
            return clip;
        }

        static UnitVisual CreateUnitVisual(
            string path,
            AnimationClip idle, AnimationClip run, AnimationClip attack, AnimationClip death, AnimationClip hit,
            Sprite portrait)
        {
            var visual = AssetDatabase.LoadAssetAtPath<UnitVisual>(path);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<UnitVisual>();
                AssetDatabase.CreateAsset(visual, path);
            }

            var so = new SerializedObject(visual);
            so.FindProperty("_idleClip").objectReferenceValue = idle;
            so.FindProperty("_runClip").objectReferenceValue = run;
            so.FindProperty("_attackClip").objectReferenceValue = attack;
            so.FindProperty("_deathClip").objectReferenceValue = death;
            so.FindProperty("_hitClip").objectReferenceValue = hit;
            so.FindProperty("_portrait").objectReferenceValue = portrait;
            var skills = so.FindProperty("_skillClips");
            skills.arraySize = 4;
            skills.GetArrayElementAtIndex(0).objectReferenceValue = attack;
            for (int i = 1; i < 4; i++)
                skills.GetArrayElementAtIndex(i).objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visual);
            return visual;
        }

        static AnimatorOverrideController GetOrCreateOverride(
            string name,
            AnimationClip idle, AnimationClip run, AnimationClip attack, AnimationClip death, AnimationClip hit)
        {
            string path = $"{VisualsRoot}/{name}/{name}.overrideController";
            if (name == "MedievalWarrior")
                path = $"{VisualsRoot}/MedievalWarrior/MedievalWarrior.overrideController";

            EnsureFolder(VisualsRoot, name == "MedievalWarrior" ? "MedievalWarrior" : name);

            var baseCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BaseController);
            if (baseCtrl == null) throw new InvalidOperationException("UnitBase.controller missing");

            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(baseCtrl);
                AssetDatabase.CreateAsset(oc, path);
            }
            else
            {
                oc.runtimeAnimatorController = baseCtrl;
            }

            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
            {
                var key = pairs[i].Key;
                if (key == null) continue;
                string n = key.name;
                AnimationClip repl = null;
                if (Contains(n, "Idle")) repl = idle;
                else if (Contains(n, "Run")) repl = run;
                else if (Contains(n, "Attack") && !Contains(n, "Skill")) repl = attack;
                else if (Contains(n, "Death")) repl = death;
                else if (Contains(n, "Hit") || Contains(n, "Take")) repl = hit;
                else if (Contains(n, "Skill")) repl = attack;
                if (repl != null)
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, repl);
            }

            oc.ApplyOverrides(pairs);
            EditorUtility.SetDirty(oc);
            return oc;
        }

        static bool Contains(string name, string token) =>
            name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        static void CreateVariantPrefab(string path, AnimatorOverrideController controller, Sprite idleSprite)
        {
            var parent = AssetDatabase.LoadAssetAtPath<GameObject>(ParentPrefab);
            if (parent == null) throw new InvalidOperationException("Parent UnitView.prefab missing");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(parent);
            try
            {
                var body = instance.transform.Find("Visual Sprites/Body");
                if (body == null) throw new InvalidOperationException("Body child missing on UnitView");

                var animator = body.GetComponent<Animator>();
                var sr = body.GetComponent<SpriteRenderer>();
                if (animator != null) animator.runtimeAnimatorController = controller;
                if (sr != null && idleSprite != null) sr.sprite = idleSprite;

                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void AssignToUnitData(string soPath, UnitVisual visual, string prefabPath, Color tint)
        {
            var unit = AssetDatabase.LoadAssetAtPath<UnitData>(soPath);
            if (unit == null)
            {
                Debug.LogError($"[BuildUnitViews] UnitData missing: {soPath}");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var so = new SerializedObject(unit);
            so.FindProperty("_visual").objectReferenceValue = visual;
            so.FindProperty("_viewPrefab").objectReferenceValue = prefab;
            so.FindProperty("_tint").colorValue = tint;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(unit);
        }

        static Sprite FirstSprite(AnimationClip clip)
        {
            if (clip == null) return null;
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var b in bindings)
            {
                if (b.propertyName != "m_Sprite") continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                if (keys != null && keys.Length > 0) return keys[0].value as Sprite;
            }

            return null;
        }

        static void EnsureFolder(string parent, string child)
        {
            if (string.IsNullOrEmpty(child))
            {
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    string p = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                    string n = Path.GetFileName(parent);
                    if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(n))
                        AssetDatabase.CreateFolder(p, n);
                }

                return;
            }

            string full = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, child);
        }

        static string ToAbsolute(string assetPath) =>
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));

        sealed class BuiltVisual
        {
            public readonly UnitVisual Visual;
            public readonly AnimatorOverrideController OverrideController;
            public readonly Sprite IdleSprite;

            public BuiltVisual(UnitVisual visual, AnimatorOverrideController oc, Sprite idle)
            {
                Visual = visual;
                OverrideController = oc;
                IdleSprite = idle;
            }
        }

        enum PackKind { Square, FixedCell, GridRows }

        sealed class Pack
        {
            public string Name;
            public PackKind Kind;
            public string Folder;
            public string SheetPath;
            public string Idle, Run, Attack, Death, Hit;
            public int CellW, CellH, Cols, Rows, Cell;
            public int IdleRow, RunRow, AttackRow, DeathRow, HitRow;
            Sprite[] _grid;

            public static Pack Square(string name, string folder, string idle, string run, string attack, string death, string hit) =>
                new Pack
                {
                    Name = name, Kind = PackKind.Square, Folder = folder,
                    Idle = idle, Run = run, Attack = attack, Death = death, Hit = hit,
                };

            public static Pack FixedCell(string name, string folder, int cellW, int cellH,
                string idle, string run, string attack, string death, string hit) =>
                new Pack
                {
                    Name = name, Kind = PackKind.FixedCell, Folder = folder,
                    CellW = cellW, CellH = cellH,
                    Idle = idle, Run = run, Attack = attack, Death = death, Hit = hit,
                };

            public static Pack GridRows(string name, string sheetPath, int cols, int rows, int cell,
                int idleRow, int runRow, int attackRow, int deathRow, int hitRow) =>
                new Pack
                {
                    Name = name, Kind = PackKind.GridRows, SheetPath = sheetPath,
                    Cols = cols, Rows = rows, Cell = cell,
                    IdleRow = idleRow, RunRow = runRow, AttackRow = attackRow,
                    DeathRow = deathRow, HitRow = hitRow,
                };

            public string Resolve(string rel)
            {
                if (Kind == PackKind.GridRows) return SheetPath;
                if (string.IsNullOrEmpty(rel)) return null;
                return $"{Folder}/{rel}";
            }

            public void CacheGridSprites()
            {
                _grid = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
                    .OfType<Sprite>()
                    .OrderBy(s =>
                    {
                        // row from name _rN_ or from rect
                        var n = s.name;
                        int idx = n.IndexOf("_r", StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            int start = idx + 2;
                            int end = n.IndexOf('_', start);
                            if (end > start && int.TryParse(n.Substring(start, end - start), out int row))
                                return row * 1000 + (int)s.rect.x;
                        }

                        return ((Rows - 1) - (int)(s.rect.y / Cell)) * 1000 + (int)s.rect.x;
                    })
                    .ToArray();
            }

            public Sprite[] RowSprites(int row)
            {
                if (_grid == null || _grid.Length == 0) return Array.Empty<Sprite>();
                return _grid.Where(s => s.name.Contains($"_r{row}_")).OrderBy(s => s.rect.x).ToArray();
            }
        }
    }
}
#endif
