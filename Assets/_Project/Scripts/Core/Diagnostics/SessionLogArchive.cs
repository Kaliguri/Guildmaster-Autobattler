using System;
using System.IO;
using Guildmaster.Core.Persistence;
using UnityEngine;

namespace Guildmaster.Core.Diagnostics
{
    /// <summary>
    /// Архив логов прошлых запусков: то, что можно прислать после прогона вдвоём.
    /// </summary>
    /// <remarks>
    /// <b>Существует потому, что Unity перезаписывает свой лог на КАЖДОМ запуске.</b> После сессии со
    /// Steam игрок закрывает игру, открывает снова — и лог, в котором был разбор, уже затёрт. Прошлый
    /// запуск сохраняется здесь под датой, и его можно отдать целиком (просьба Макса 04.08.2026 — «Мы
    /// не можем как-то сохранять логи с моих игр сессий со Steam? Для последующего анализа»).
    /// <para><b>Копируем ПРЕДЫДУЩИЙ лог, а не текущий:</b> текущий Unity держит открытым и дописывает,
    /// а рядом с ним лежит `Player-prev.log` — уже закрытый и целый. Копия делается один раз на
    /// запуск, до того как игра начнёт что-либо писать.</para>
    /// <para><b>Живёт рядом с сейвами, но ВНЕ облачной маски</b> (<c>Local/Logs</c>): лог — это про
    /// компьютер и про конкретный прогон, синхронизировать его между машинами незачем, а весит он
    /// заметно больше сейва.</para>
    /// </remarks>
    public static class SessionLogArchive
    {
        /// <summary>Сколько прогонов держим. Дальше старые уходят: лог быстро растёт, а нужен свежий.</summary>
        public const int KeepRuns = 10;

        /// <summary>Куда складываем — <c>Local/Logs</c> под корнем данных игры.</summary>
        public static string Folder => Path.Combine(GameDataPath.Root, "Local", "Logs");

        /// <summary>
        /// Сохранить лог прошлого запуска. Зовётся один раз на старте игры.
        /// </summary>
        /// <param name="stamp">
        /// Метка времени для имени файла. Приходит снаружи, чтобы имя было предсказуемым в тесте.
        /// </param>
        public static string ArchivePrevious(DateTime stamp)
        {
            try
            {
                string previous = PreviousLogPath();
                if (string.IsNullOrEmpty(previous) || !File.Exists(previous)) return null;

                Directory.CreateDirectory(Folder);

                string target = Path.Combine(Folder, $"run-{stamp:yyyyMMdd-HHmmss}.log");
                File.Copy(previous, target, overwrite: true);

                Trim();
                return target;
            }
            catch (Exception e)
            {
                // Архив — удобство, а не работа игры: не смогли скопировать (лог занят, диск полон) —
                // говорим и живём дальше. Падать из-за отладочной копии было бы обидно.
                Debug.LogWarning($"[SessionLogArchive] лог прошлого запуска не сохранён: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Где Unity держит лог прошлого запуска. Рядом с текущим и с тем же именем плюс <c>-prev</c>.
        /// </summary>
        private static string PreviousLogPath()
        {
            string current = Application.consoleLogPath;
            if (string.IsNullOrEmpty(current)) return null;

            string dir  = Path.GetDirectoryName(current);
            string name = Path.GetFileNameWithoutExtension(current);
            string ext  = Path.GetExtension(current);

            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, $"{name}-prev{ext}");
        }

        /// <summary>Оставить последние <see cref="KeepRuns"/> прогонов.</summary>
        private static void Trim()
        {
            var files = new DirectoryInfo(Folder).GetFiles("run-*.log");
            if (files.Length <= KeepRuns) return;

            Array.Sort(files, (a, b) => b.Name.CompareTo(a.Name)); // имя = дата, поэтому сортировка по нему
            for (int i = KeepRuns; i < files.Length; i++)
            {
                try { files[i].Delete(); }
                catch (Exception e) { Debug.LogWarning($"[SessionLogArchive] не удалить {files[i].Name}: {e.Message}"); }
            }
        }
    }
}
