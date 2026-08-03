#if MCP_FOR_UNITY
using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Presentation.Editor.Agent
{
    /// <summary>
    /// Снимок вида для агента: одиночный кадр или контактный лист последовательности. Два источника —
    /// живая игра в play mode и камера открытой сцены.
    /// </summary>
    /// <remarks>
    /// <para>Зачем. Без этого агент не видит результата своей работы и итерирует вслепую, а глазами
    /// работает Макс — каждый раз, своим временем. Это самое дорогое звено нашего пайплайна, и
    /// разомкнуто оно было буквально: в <c>Assets/_Project</c> не было ни одного захвата кадра.</para>
    /// <para><b>Почему два механизма в одном туле.</b> Вне play mode кадр снимается синхронно: камера
    /// уже есть, рендер по требованию, ответ в том же вызове. В play mode так нельзя — кадры игры
    /// идут ПОСЛЕ возврата из обработчика, и снять сорок штук за один вызов нечем. Поэтому живой
    /// захват копит кадры на <see cref="EditorApplication.update"/> и отвечает поллингом
    /// (<c>action=status</c>), а синхронный отвечает сразу. Развилка спрятана внутрь: снаружи это
    /// один инструмент с одним смыслом — «покажи, что происходит».</para>
    /// <para><b>Кадры считаются по тикам показа, а не по секундам.</b> Показ у нас идёт с лагом от
    /// симуляции, и подпись временем свела бы кадр с событием симуляции, которого в этом кадре ещё
    /// нет. Соответствие «индекс кадра → тик» уходит числом в ответ, на самом листе текста нет —
    /// почему так, см. <see cref="FrameSheet"/>.</para>
    /// <para>Пишем только в <c>Temp/</c>: захваты не коммитятся. Эталоны визуального регресса, когда
    /// дойдут руки, получат свой дом отдельно.</para>
    /// </remarks>
    [McpForUnityTool("capture_view",
        Description = "Снять вид игры в PNG: одиночный кадр или контактный лист последовательности. " +
                      "В play mode копит кадры и отвечает поллингом; вне play mode отвечает сразу.",
        RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300)]
    public static class CaptureViewTool
    {
        public const string OutputDir = "Temp/agent-capture";

        /// <summary>
        /// Схема параметров для агента. Имена свойств уходят в схему <b>как есть</b>
        /// (<c>ToolDiscoveryService</c> берёт <c>prop.Name</c> без приведения к snake_case).
        /// </summary>
        public class Parameters
        {
            [ToolParameter("capture — снять, status — опросить идущий захват, cancel — бросить.",
                Required = false, DefaultValue = "capture")]
            public string action { get; set; }

            [ToolParameter("Сколько кадров. 1 — одиночный кадр, больше — контактный лист.",
                Required = false, DefaultValue = "1")]
            public int frames { get; set; }

            [ToolParameter("Через сколько кадров игры брать следующий снимок. Только для play mode.",
                Required = false, DefaultValue = "2")]
            public int every { get; set; }

            [ToolParameter("Сторона кадра в пикселях.", Required = false, DefaultValue = "512")]
            public int size { get; set; }

            [ToolParameter("Колонок в контактном листе.", Required = false, DefaultValue = "6")]
            public int columns { get; set; }

            [ToolParameter("Куда писать PNG. Пусто — в Temp/agent-capture с именем по времени.",
                Required = false)]
            public string output { get; set; }
        }

        /// <summary>Заказ на живой захват; переживает domain reload через <c>McpJobStateStore</c>.</summary>
        [Serializable]
        private sealed class Job
        {
            public int Frames;
            public int Every;
            public int Size;
            public int Columns;
            public string Output;

            public int Captured;
            public int Skipped;
            public bool Done;
            public string Error;
            public string Path;
            public List<int> Ticks = new();
        }

        private const string JobKey = "capture_view";

        // Пиксели живут в памяти процесса, а не в состоянии job: сериализовать сорок кадров по
        // четверти мегабайта — верный способ утопить сохранение состояния ради данных, которые всё
        // равно бессмысленны после перезагрузки домена.
        private static readonly List<Color[]> Pixels = new();
        private static bool _pumping;

        public static object HandleCommand(JObject @params)
        {
            string action = (@params?["action"]?.ToString() ?? "capture").ToLowerInvariant();

            switch (action)
            {
                case "capture": return HandleCapture(@params);
                case "status": return HandleStatus();
                case "cancel": return HandleCancel();
                default:
                    return new ErrorResponse($"unknown_action: '{action}'. Есть capture, status, cancel.");
            }
        }

        private static object HandleCapture(JObject @params)
        {
            int frames = Mathf.Clamp(@params?["frames"]?.ToObject<int?>() ?? 1, 1, 120);
            int every = Mathf.Clamp(@params?["every"]?.ToObject<int?>() ?? 2, 1, 60);
            int size = Mathf.Clamp(@params?["size"]?.ToObject<int?>() ?? 512, 64, 2048);
            int columns = Mathf.Clamp(@params?["columns"]?.ToObject<int?>() ?? 6, 1, 12);
            string output = @params?["output"]?.ToString();

            if (EditorApplication.isPlaying)
                return StartLiveCapture(frames, every, size, columns, output);

            if (frames > 1)
                return new ErrorResponse(
                    "sequence_needs_play_mode: вне play mode кадр один — движение снимать нечему. " +
                    "Войди в play mode (manage_editor), либо проси frames=1.");

            return CaptureSceneFrame(size, output);
        }

        // ── вне play mode: камера открытой сцены, синхронно ─────────────────────────────────

        private static object CaptureSceneFrame(int size, string output)
        {
            // Пустой кадр — самый дорогой из возможных ответов: агент читает чёрный PNG как «игра
            // рисует чёрное» и уходит чинить то, что не сломано. Поэтому подменная камера и камера,
            // которой нечего рисовать, называются вслух, а не молча дают снимок пустоты.
            string warning = null;

            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] all = Camera.allCameras;
                if (all.Length == 0)
                    return new ErrorResponse("no_camera: в открытой сцене нет ни одной камеры — " +
                                             "снимать нечем.");
                cam = all[0];
                warning = $"no_main_camera: камеры с тегом MainCamera нет, снято через «{cam.name}».";
            }

            if (cam.cullingMask == 0)
                warning = (warning == null ? "" : warning + " ") +
                          $"empty_culling_mask: «{cam.name}» не рендерит ни одного слоя — кадр будет " +
                          "пустым. Вне play mode боевые сцены собираются в рантайме: входи в play mode " +
                          "(manage_editor) или открывай сцену с готовым содержимым.";

            RenderTexture rt = null;
            RenderTexture previousTarget = cam.targetTexture;
            try
            {
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render();

                Color[] pixels = FrameSheet.ReadBack(rt, size, size);
                var tex = FrameSheet.NewSheet(size, size, Color.clear);
                tex.SetPixels(pixels);
                tex.Apply();

                string path = Write(tex, output, "frame");
                UnityEngine.Object.DestroyImmediate(tex);

                return new SuccessResponse(warning == null ? "Кадр снят." : "Кадр снят, но смотри warning.",
                                           new { path, camera = cam.name, size, warning });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"capture_failed: {e.Message}");
            }
            finally
            {
                cam.targetTexture = previousTarget;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            }
        }

        // ── play mode: накопление кадров на update, ответ поллингом ─────────────────────────

        private static object StartLiveCapture(int frames, int every, int size, int columns, string output)
        {
            Job running = McpJobStateStore.LoadState<Job>(JobKey);
            if (running != null && !running.Done)
                return new ErrorResponse("already_capturing: захват уже идёт. Спроси status или cancel.");

            Pixels.Clear();
            var job = new Job
            {
                Frames = frames,
                Every = every,
                Size = size,
                Columns = columns,
                Output = output
            };
            McpJobStateStore.SaveState(JobKey, job);

            if (!_pumping)
            {
                EditorApplication.update += Pump;
                _pumping = true;
            }

            return new PendingResponse($"Снимаю {frames} кадров через каждые {every}.", 0.5,
                new { frames, every, captured = 0 });
        }

        private static void Pump()
        {
            Job job = McpJobStateStore.LoadState<Job>(JobKey);
            if (job == null || job.Done) { StopPump(); return; }

            if (!EditorApplication.isPlaying)
            {
                Fail(job, "play_mode_left: игру закрыли посреди захвата.");
                return;
            }

            // Прореживание живёт здесь, а не в счётчике кадров игры: редакторный update и кадр игрока
            // не одно и то же, но для «снимай каждый N-й» этой точности достаточно, а зависимость от
            // внутренностей цикла игры не заводится.
            if (++job.Skipped < job.Every)
            {
                McpJobStateStore.SaveState(JobKey, job);
                return;
            }
            job.Skipped = 0;

            try
            {
                Camera cam = Camera.main ?? (Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null);
                if (cam == null) { Fail(job, "no_camera: в работающей игре не нашлось камеры."); return; }

                var rt = new RenderTexture(job.Size, job.Size, 24, RenderTextureFormat.ARGB32,
                                           RenderTextureReadWrite.sRGB);
                RenderTexture previousTarget = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = previousTarget;

                Pixels.Add(FrameSheet.ReadBack(rt, job.Size, job.Size));
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);

                job.Ticks.Add(Time.frameCount);
                job.Captured = Pixels.Count;

                if (job.Captured >= job.Frames) Finish(job);
                else McpJobStateStore.SaveState(JobKey, job);
            }
            catch (Exception e)
            {
                Fail(job, $"capture_failed: {e.Message}");
            }
        }

        private static void Finish(Job job)
        {
            try
            {
                Texture2D sheet = job.Frames == 1
                    ? Single(Pixels[0], job.Size)
                    : FrameSheet.ComposeContactSheet(Pixels, job.Size, new FrameSheet.Options { Columns = job.Columns });

                job.Path = Write(sheet, job.Output, job.Frames == 1 ? "frame" : "sheet");
                UnityEngine.Object.DestroyImmediate(sheet);
                job.Done = true;
                McpJobStateStore.SaveState(JobKey, job);
            }
            catch (Exception e)
            {
                Fail(job, $"compose_failed: {e.Message}");
                return;
            }
            StopPump();
        }

        private static Texture2D Single(Color[] pixels, int size)
        {
            var tex = FrameSheet.NewSheet(size, size, Color.clear);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static void Fail(Job job, string error)
        {
            job.Done = true;
            job.Error = error;
            McpJobStateStore.SaveState(JobKey, job);
            StopPump();
        }

        private static void StopPump()
        {
            if (!_pumping) return;
            EditorApplication.update -= Pump;
            _pumping = false;
            Pixels.Clear();
        }

        private static object HandleStatus()
        {
            Job job = McpJobStateStore.LoadState<Job>(JobKey);
            if (job == null) return new ErrorResponse("no_job: захват не запускали.");

            var data = new { captured = job.Captured, frames = job.Frames, ticks = job.Ticks, path = job.Path };

            if (!job.Done)
                return new PendingResponse($"Снято {job.Captured} из {job.Frames}.", 0.5, data);

            if (!string.IsNullOrEmpty(job.Error)) return new ErrorResponse(job.Error, data);

            return new SuccessResponse(
                job.Frames == 1 ? "Кадр снят." : $"Лист из {job.Captured} кадров собран.", data);
        }

        private static object HandleCancel()
        {
            Job job = McpJobStateStore.LoadState<Job>(JobKey);
            StopPump();
            McpJobStateStore.ClearState(JobKey);
            return new SuccessResponse(job == null ? "Нечего отменять." : "Захват брошен.");
        }

        private static string Write(Texture2D tex, string requested, string kind)
        {
            string path = string.IsNullOrWhiteSpace(requested)
                ? Path.GetFullPath(Path.Combine(OutputDir,
                    $"{kind}_{DateTime.Now:yyyyMMdd_HHmmss}.png"))
                : Path.GetFullPath(requested);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return path;
        }
    }
}
#endif
