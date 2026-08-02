#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Renders a skeletal AnimationClip into a still image so it can be judged without play mode:
    /// a contact sheet (frames laid out in a grid) or an onion skin (frames stacked in one cell).
    ///
    /// Everything happens in a preview scene, so the open scene is never touched.
    /// </summary>
    public static class AnimationLabRenderer
    {
        public const string DefaultOutputDir = "Temp/anim-lab";

        public sealed class Options
        {
            /// <summary>Frames to sample. 0 = auto (every clip frame, capped at 24). Ignored when <see cref="InBetweens"/> is set.</summary>
            public int Frames;
            /// <summary>
            /// Sample the clip's own keyframes plus this many in-betweens between each neighbouring pair,
            /// instead of spacing samples evenly. Even spacing lies exactly where it matters: a strike
            /// lives in three frames and an even sheet steps straight over it, while the long recovery
            /// gets most of the cells. 0 = off.
            /// </summary>
            public int InBetweens;
            public int Columns = 6;
            public int CellSize = 192;
            /// <summary>Extra room around the widest pose. 1.0 = tight fit.</summary>
            public float Padding = 1.15f;
            public Color Background = new Color(0.10f, 0.10f, 0.12f, 1f);
            public Color Divider = new Color(0.25f, 0.25f, 0.30f, 1f);
            /// <summary>Frames carrying an AnimationEvent get a brass tick above them.</summary>
            public Color EventMarker = new Color(0.85f, 0.65f, 0.25f, 1f);
            public string OutputPath;

            /// <summary>
            /// Надстройки, которые кладутся ПОВЕРХ базового клипа — так же, как это делают слои Animator в
            /// бою. Нужны потому, что игра никогда не играет один клип: щит встаёт поверх бега, удар идёт
            /// поверх шага, таз получает просадку удара аддитивом. Читаются они вместе или нет — вопрос про
            /// КОМБИНАЦИЮ (Макс, 30.07). Пусто = обычный лист по одному клипу.
            /// </summary>
            /// <remarks>
            /// Стек, а не одна надстройка: с 30.07 удар живёт сразу на двух слоях (Override на верх тела и
            /// Additive на таз), и лист, умеющий только один оверлей, показывал бы позу, которой в игре нет.
            /// </remarks>
            public RigLayerBlend.Layer[] Layers;

            /// <summary>
            /// Обратный случай: САМ клип листа идёт слоем поверх другого тела — так живёт удар с 30.07,
            /// когда ноги бегут, а свинг едет надстройкой. <see cref="Layers"/> отвечает на «что лежит
            /// поверх этого клипа», а это поле — на «подо что этот клип подложен». Null = нет подложки.
            /// </summary>
            public RigLayerBlend.Composition Composition;
        }

        public sealed class Result
        {
            public string Path;
            public int FrameCount;
            public float ClipLength;
            /// <summary>Sample index -> clip time, so a rendered cell can be named in seconds.</summary>
            public float[] Times;
            public int[] EventFrames;

            public override string ToString()
            {
                return $"{Path} ({FrameCount} frames over {ClipLength:F2}s)";
            }
        }

        /// <summary>Frames side by side in a grid — reads the pose sequence.</summary>
        public static Result RenderContactSheet(GameObject prefab, AnimationClip clip, Options options = null)
        {
            return Render(prefab, clip, options, onionSkin: false);
        }

        /// <summary>All frames stacked in one cell, older poses fainter — reads the arc of motion.</summary>
        public static Result RenderOnionSkin(GameObject prefab, AnimationClip clip, Options options = null)
        {
            return Render(prefab, clip, options, onionSkin: true);
        }

        /// <summary>
        /// Поза кадра: базовый клип, а поверх — надстройка через маску, если её попросили. Один вход для
        /// обоих проходов рендера, чтобы «что мы показываем» не разъезжалось между листом и onion skin.
        /// </summary>
        static void SampleComposed(GameObject unit, AnimationClip clip, float time, Options options)
        {
            // Сначала подложка (клип листа сам едет слоем), потом надстройки поверх — порядок тот же, что
            // у Animator, и обратный порядок дал бы позу, которой в игре не бывает.
            if (options.Composition != null && options.Composition.Active)
            {
                RigLayerBlend.SampleTraced(unit, clip, time, options.Composition);
                RigLayerBlend.Fold(unit, options.Layers);   // поверх готовой композиции, БЕЗ пересэмпла базы
                return;
            }

            if (options.Layers == null || options.Layers.Length == 0)
            {
                clip.SampleAnimation(unit, time);
                return;
            }

            RigLayerBlend.Sample(unit, clip, time, options.Layers);
        }

        /// <summary>Evenly spaced samples, snapped to real clip frames.</summary>
        static float[] EvenTimes(AnimationClip clip, float frameRate, int requested)
        {
            int clipFrames = Mathf.Max(1, Mathf.RoundToInt(clip.length * frameRate) + 1);
            int samples = Mathf.Max(1, requested > 0 ? requested : Mathf.Min(clipFrames, 24));
            var times = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = samples == 1 ? 0f : clip.length * i / (samples - 1);
                // Snap to a real clip frame: sampling between frames blurs the timing we are judging.
                times[i] = Mathf.Round(t * frameRate) / frameRate;
            }
            return times;
        }

        /// <summary>
        /// The clip's own keyframe times, with <paramref name="inBetweens"/> extra samples inside every
        /// gap. Poses land on the cells that were authored, and the fast stretches finally get shown:
        /// what reads as a broken swing is usually the movement BETWEEN keys, which an even sheet skips.
        /// </summary>
        static float[] KeyframeTimes(AnimationClip clip, float frameRate, int inBetweens)
        {
            var keys = new SortedSet<float>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                foreach (var key in curve.keys) keys.Add(Mathf.Round(key.time * frameRate) / frameRate);
            }
            if (keys.Count == 0) return EvenTimes(clip, frameRate, 0);

            var ordered = new List<float>(keys);
            var times = new List<float>(ordered.Count * (inBetweens + 1));
            for (int i = 0; i < ordered.Count; i++)
            {
                times.Add(ordered[i]);
                if (i + 1 >= ordered.Count) break;
                for (int k = 1; k <= inBetweens; k++)
                {
                    float t = Mathf.Lerp(ordered[i], ordered[i + 1], k / (float)(inBetweens + 1));
                    float snapped = Mathf.Round(t * frameRate) / frameRate;
                    // Keys closer together than the in-between step would otherwise produce duplicates.
                    if (snapped > times[times.Count - 1] + 1e-4f) times.Add(snapped);
                }
            }
            return times.ToArray();
        }

        static Result Render(GameObject prefab, AnimationClip clip, Options options, bool onionSkin)
        {
            if (prefab == null) throw new System.ArgumentNullException(nameof(prefab));
            if (clip == null) throw new System.ArgumentNullException(nameof(clip));

            options ??= new Options();
            int cell = Mathf.Max(32, options.CellSize);

            float frameRate = clip.frameRate > 0f ? clip.frameRate : 60f;
            float[] times = options.InBetweens > 0
                ? KeyframeTimes(clip, frameRate, options.InBetweens)
                : EvenTimes(clip, frameRate, options.Frames);
            int samples = times.Length;

            var scene = EditorSceneManager.NewPreviewScene();
            GameObject unit = null, camGo = null;
            RenderTexture rt = null;
            try
            {
                unit = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                SceneManager.MoveGameObjectToScene(unit, scene);
                unit.transform.position = Vector3.zero;
                unit.transform.rotation = Quaternion.identity;

                var renderers = unit.GetComponentsInChildren<Renderer>(includeInactive: false);
                if (renderers.Length == 0)
                    throw new System.InvalidOperationException($"'{prefab.name}' has no renderers to draw.");

                // Pass 1: one framing for the whole clip. Per-frame framing would hide the very
                // travel we are trying to see.
                var bounds = MeasureClip(unit, clip, times, renderers, options);

                camGo = new GameObject("AnimationLabCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = options.Background;
                cam.cullingMask = ~0;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.aspect = 1f;
                cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * Mathf.Max(1f, options.Padding);
                cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
                cam.transform.rotation = Quaternion.identity;

                rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;

                // Pass 2: draw.
                var frames = new List<Color[]>(samples);
                for (int i = 0; i < samples; i++)
                {
                    SampleComposed(unit, clip, times[i], options);
                    cam.Render();
                    frames.Add(Presentation.Editor.FrameSheet.ReadBack(rt, cell, cell));
                }

                var eventFrames = FindEventFrames(clip, times);
                var layout = ToSheetOptions(options);
                var sheet = onionSkin
                    ? Presentation.Editor.FrameSheet.ComposeOnionSkin(frames, cell, layout)
                    : Presentation.Editor.FrameSheet.ComposeContactSheet(frames, cell, layout, eventFrames);

                string path = ResolveOutputPath(options.OutputPath, clip, onionSkin);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);

                return new Result
                {
                    Path = path,
                    FrameCount = samples,
                    ClipLength = clip.length,
                    Times = times,
                    EventFrames = eventFrames
                };
            }
            finally
            {
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (unit != null) Object.DestroyImmediate(unit);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // options — ради надстройки: кадрирование обязано считаться по ТОЙ ЖЕ позе, которую потом рисуем,
        // иначе поднятый щит вылезет за границы листа.
        static Bounds MeasureClip(GameObject unit, AnimationClip clip, float[] times, Renderer[] renderers,
                                  Options options)
        {
            var bounds = new Bounds(unit.transform.position, Vector3.zero);
            bool seeded = false;

            foreach (float t in times)
            {
                SampleComposed(unit, clip, t, options);
                foreach (var r in renderers)
                {
                    if (!r.enabled) continue;
                    if (!seeded) { bounds = r.bounds; seeded = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            if (!seeded) bounds = new Bounds(unit.transform.position, Vector3.one);
            if (bounds.extents.x < 0.001f || bounds.extents.y < 0.001f)
                bounds.extents = new Vector3(Mathf.Max(bounds.extents.x, 0.5f), Mathf.Max(bounds.extents.y, 0.5f), bounds.extents.z);
            return bounds;
        }

        /// <summary>Разметка листа лаборатории в терминах общей склейки.</summary>
        static Presentation.Editor.FrameSheet.Options ToSheetOptions(Options options) => new()
        {
            Columns = options.Columns,
            Background = options.Background,
            Divider = options.Divider,
            EventMarker = options.EventMarker
        };

        static int[] FindEventFrames(AnimationClip clip, float[] times)
        {
            var events = AnimationUtility.GetAnimationEvents(clip);
            if (events == null || events.Length == 0) return System.Array.Empty<int>();

            var marked = new List<int>(events.Length);
            foreach (var e in events)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < times.Length; i++)
                {
                    float d = Mathf.Abs(times[i] - e.time);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                if (!marked.Contains(best)) marked.Add(best);
            }
            return marked.ToArray();
        }


        static string ResolveOutputPath(string requested, AnimationClip clip, bool onionSkin)
        {
            if (!string.IsNullOrEmpty(requested)) return Path.GetFullPath(requested);
            string suffix = onionSkin ? "_onion" : "_sheet";
            return Path.GetFullPath(Path.Combine(DefaultOutputDir, clip.name + suffix + ".png"));
        }
    }
}
#endif
