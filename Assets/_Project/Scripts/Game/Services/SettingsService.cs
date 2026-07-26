using System;
using Guildmaster.Core.Audio;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Settings;
using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Реализация <see cref="ISettingsService"/>. Персист — <b>за швом <see cref="ISaveService"/></b>, ключ
    /// <c>prefs</c>: атомарная запись, бэкап и версия схемы достаются даром, а второго владельца записи на
    /// диск не появляется. Прежде сервис писал файл сам (<c>File.WriteAllText</c>) — без всего этого.
    /// Дефолты первого запуска — из <see cref="GameConfig"/>. Значения применяются в аудио сразу (живой
    /// драг слайдера).
    /// <para><b>Что здесь НЕ хранится:</b> настройки дисплея (разрешение, режим окна, качество). По ТЗ
    /// [[save-system]] §3 они машинно-локальные и в Steam Cloud не едут, иначе второй компьютер получает
    /// чужое разрешение. Отдельного файла под них пока нет — в игре нет ни одной такой настройки; появится
    /// первая, тогда и заводить (пустой файл был бы мёртвым кодом).</para>
    /// <para>Entry point: <see cref="Start"/> зовёт <see cref="Load"/> на старте сессии. Аудио-шина может быть
    /// ещё невалидна до загрузки банков — применение тихо не пройдёт (как и <see cref="IAudioService.SetMusicVolume"/>);
    /// живые правки в игре применяются уже корректно.</para>
    /// </summary>
    public sealed class SettingsService : ISettingsService, IStartable
    {
        private const string SaveKey = "prefs";

        private readonly IAudioService _audio;
        private readonly GameConfig _config;
        private readonly ISaveService _save;
        private AudioVolumeSettings _audio01;
        private GameplaySettings _gameplay;

        public SettingsService(IAudioService audio, GameConfig config, ISaveService save)
        {
            _audio = audio;
            _config = config;
            _save = save;
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
            _save.Save(SaveKey, new PersistModel
            {
                Master                 = _audio01.Master,
                Music                  = _audio01.Music,
                Sfx                    = _audio01.Sfx,
                CardAnimations         = _gameplay.CardAnimations,
                CardAttackAnimation    = _gameplay.CardAttackAnimation,
                AlwaysDetailedTooltips = _gameplay.AlwaysDetailedTooltips,
            });
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

        // Читает сейв в _audio01 + _gameplay. Отсутствующее в файле поле подхватывает дефолт своего
        // владельца — поэтому старый файл (лишь громкости) даёт новые тумблеры ВКЛ, а не default(bool)=false.
        // Нет файла/ошибка/чужая версия → дефолты целиком.
        private void ReadFromDisk()
        {
            AudioVolumeSettings audioDefaults = Defaults();
            // Геймплейная половина — из своего владельца, ровно как аудио строкой выше. Раньше она
            // была набрана здесь литералами: те же значения вторым местом, которое разъехалось бы с
            // GameplaySettings.Defaults() на первой же правке дефолта (аудит 2026-07-26, T-27).
            GameplaySettings gameplayDefaults = GameplaySettings.Defaults();

            SaveLoadResult<PersistModel> loaded = _save.TryLoad<PersistModel>(SaveKey);
            PersistModel model = loaded.IsOk ? loaded.Value : new PersistModel();

            if (loaded.IsBlocked)
                Debug.LogWarning($"[Settings] Настройки записаны версией {loaded.SavedGameVersion} " +
                                 $"({loaded.Status}) — беру дефолты, файл не трогаю");

            _audio01 = new AudioVolumeSettings(
                Clamp01(model.Master ?? audioDefaults.Master),
                Clamp01(model.Music  ?? audioDefaults.Music),
                Clamp01(model.Sfx    ?? audioDefaults.Sfx));

            _gameplay = new GameplaySettings(
                model.CardAnimations         ?? gameplayDefaults.CardAnimations,
                model.CardAttackAnimation    ?? gameplayDefaults.CardAttackAnimation,
                model.AlwaysDetailedTooltips ?? gameplayDefaults.AlwaysDetailedTooltips);
        }

        /// <summary>
        /// Плоская форма персиста. Поля <b>nullable намеренно</b>: «в файле поля нет» обязано отличаться от
        /// «в файле лежит false/0». Иначе сейв, записанный до появления тумблера, читался бы как «тумблер
        /// выключён», и настройка молча переключалась бы у игрока сама.
        /// </summary>
        [Serializable]
        [SaveSchema(1)]
        private sealed class PersistModel
        {
            public float? Master;
            public float? Music;
            public float? Sfx;
            public bool?  CardAnimations;
            public bool?  CardAttackAnimation;
            public bool?  AlwaysDetailedTooltips;
        }

        private void ApplyAll()
        {
            _audio.SetMasterVolume(_audio01.Master);
            _audio.SetMusicVolume(_audio01.Music);
            _audio.SetSfxVolume(_audio01.Sfx);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
