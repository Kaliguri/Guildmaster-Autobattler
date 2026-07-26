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
        private GameplaySettings _gameplay;

        public SettingsService(IAudioService audio, GameConfig config)
        {
            _audio = audio;
            _config = config;
            _audio01 = Defaults();
            _gameplay = GameplaySettings.Defaults();
        }

        public AudioVolumeSettings Audio => _audio01;
        public GameplaySettings Gameplay => _gameplay;
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

        // Геймплей-тумблеры не имеют «живого применения» в аудио — их читает инвентарь при открытии.
        // Поднимаем Changed, чтобы UI-биндинг (тумблеры/Cancel/Defaults) синхронизировался.
        public void SetCardAnimations(bool enabled)
        {
            _gameplay.CardAnimations = enabled;
            Changed?.Invoke();
        }

        public void SetCardAttackAnimation(bool enabled)
        {
            _gameplay.CardAttackAnimation = enabled;
            Changed?.Invoke();
        }

        public void SetAlwaysDetailedTooltips(bool enabled)
        {
            _gameplay.AlwaysDetailedTooltips = enabled;
            Changed?.Invoke();
        }

        public void Load()
        {
            ReadFromDisk();
            ApplyAll();
            Changed?.Invoke();
        }

        public void Save()
        {
            try
            {
                var model = new PersistModel
                {
                    Master              = _audio01.Master,
                    Music               = _audio01.Music,
                    Sfx                 = _audio01.Sfx,
                    CardAnimations      = _gameplay.CardAnimations,
                    CardAttackAnimation = _gameplay.CardAttackAnimation,
                    AlwaysDetailedTooltips = _gameplay.AlwaysDetailedTooltips,
                };
                File.WriteAllText(FilePath, JsonUtility.ToJson(model));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Не удалось сохранить настройки: {e.Message}");
            }
        }

        public void ResetToDefaults()
        {
            _audio01 = Defaults();
            _gameplay = GameplaySettings.Defaults();
            ApplyAll();
            Changed?.Invoke();
        }

        private AudioVolumeSettings Defaults() =>
            new AudioVolumeSettings(_config.DefaultMasterVolume, _config.DefaultMusicVolume, _config.DefaultSfxVolume);

        // Читает файл в _audio01 + _gameplay. Дефолты берутся из модели ДО чтения (FromJsonOverwrite
        // трогает только присутствующие в JSON поля) — поэтому старый файл (лишь громкости) корректно
        // подхватит новые тумблеры как ВКЛ, а не как default(bool)=false. Нет файла/ошибка → дефолты.
        private void ReadFromDisk()
        {
            AudioVolumeSettings audioDefaults = Defaults();
            var model = new PersistModel
            {
                Master              = audioDefaults.Master,
                Music               = audioDefaults.Music,
                Sfx                 = audioDefaults.Sfx,
                CardAnimations      = true,
                CardAttackAnimation = true,
                AlwaysDetailedTooltips = false, // подсказки по умолчанию краткие; подробности — по Shift
            };

            try
            {
                if (File.Exists(FilePath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), model);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Не удалось прочитать настройки, беру дефолты: {e.Message}");
            }

            _audio01  = new AudioVolumeSettings(Clamp01(model.Master), Clamp01(model.Music), Clamp01(model.Sfx));
            _gameplay = new GameplaySettings(model.CardAnimations, model.CardAttackAnimation,
                model.AlwaysDetailedTooltips);
        }

        // Плоская форма персиста (совместима со старым файлом, где были только Master/Music/Sfx).
        [Serializable]
        private sealed class PersistModel
        {
            public float Master;
            public float Music;
            public float Sfx;
            public bool  CardAnimations;
            public bool  CardAttackAnimation;
            public bool  AlwaysDetailedTooltips;
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
