using System;
using System.IO;
using Guildmaster.Core.Audio;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реализация <see cref="ISettingsService"/>. Персист — JSON-файл в <c>Application.persistentDataPath</c>
    /// (без зависимостей, доступно из asmdef; ES3 сейчас в предопределённой сборке и из Game недоступен —
    /// свап на ES3 тривиален, весь персист скрыт за интерфейсом). Дефолты первого запуска — из
    /// <see cref="GameConfig"/>. Значения применяются в аудио сразу (живой драг слайдера).
    /// <para>Entry point: <see cref="Start"/> зовёт <see cref="Load"/> на старте сессии. Аудио-шина может быть
    /// ещё невалидна до загрузки банков — применение тихоно-опнется (как и <see cref="IAudioService.SetMusicVolume"/>);
    /// живые правки в игре применяются уже корректно.</para>
    /// </summary>
    public sealed class SettingsService : ISettingsService, IStartable
    {
        private readonly IAudioService _audio;
        private readonly GameConfig _config;
        private AudioVolumeSettings _audio01;

        public SettingsService(IAudioService audio, GameConfig config)
        {
            _audio = audio;
            _config = config;
            _audio01 = Defaults();
        }

        public AudioVolumeSettings Audio => _audio01;
        public event Action Changed;

        void IStartable.Start() => Load();

        public void SetMasterVolume(float volume01)
        {
            _audio01.Master = Clamp01(volume01);
            _audio.SetMasterVolume(_audio01.Master);
            Changed?.Invoke();
        }

        public void SetMusicVolume(float volume01)
        {
            _audio01.Music = Clamp01(volume01);
            _audio.SetMusicVolume(_audio01.Music);
            Changed?.Invoke();
        }

        public void SetSfxVolume(float volume01)
        {
            _audio01.Sfx = Clamp01(volume01);
            _audio.SetSfxVolume(_audio01.Sfx);
            Changed?.Invoke();
        }

        public void Load()
        {
            var loaded = ReadFromDisk(out bool ok);
            _audio01 = ok ? loaded : Defaults();
            ApplyAll();
            Changed?.Invoke();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_audio01));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Не удалось сохранить настройки: {e.Message}");
            }
        }

        public void ResetToDefaults()
        {
            _audio01 = Defaults();
            ApplyAll();
            Changed?.Invoke();
        }

        private AudioVolumeSettings Defaults() =>
            new AudioVolumeSettings(_config.DefaultMasterVolume, _config.DefaultMusicVolume, _config.DefaultSfxVolume);

        private AudioVolumeSettings ReadFromDisk(out bool ok)
        {
            ok = false;
            try
            {
                if (!File.Exists(FilePath)) return default;
                var data = JsonUtility.FromJson<AudioVolumeSettings>(File.ReadAllText(FilePath));
                data.Master = Clamp01(data.Master);
                data.Music = Clamp01(data.Music);
                data.Sfx = Clamp01(data.Sfx);
                ok = true;
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Не удалось прочитать настройки, беру дефолты: {e.Message}");
                return default;
            }
        }

        private void ApplyAll()
        {
            _audio.SetMasterVolume(_audio01.Master);
            _audio.SetMusicVolume(_audio01.Music);
            _audio.SetSfxVolume(_audio01.Sfx);
        }

        private static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
