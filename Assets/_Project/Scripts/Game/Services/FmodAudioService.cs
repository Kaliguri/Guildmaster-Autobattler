using FMODUnity;
using Guildmaster.Core.Audio;
using Guildmaster.Presentation.Audio;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// FMOD-реализация <see cref="IAudioService"/> (вики impl «09» §П3). Резолвнутый ключ от
    /// <see cref="AudioResolver"/> → <see cref="EventReference"/> через <see cref="AudioCatalog"/> →
    /// <c>PlayOneShot</c>. Пустой <see cref="EventReference"/> (нет события/банка) — тихо пропускаем,
    /// без ошибок FMOD, поэтому пустой каталог безопасен. Игровая логика FMOD напрямую не трогает —
    /// только через этот класс (правило CLAUDE.md; тот же приём, что <see cref="UnityAudioService"/>).
    /// </summary>
    public sealed class FmodAudioService : IAudioService
    {
        private readonly AudioCatalog _catalog;

        public FmodAudioService(AudioCatalog catalog) => _catalog = catalog;

        public void Play(string soundKey)
        {
            if (_catalog == null || string.IsNullOrEmpty(soundKey)) return;
            if (!_catalog.TryGetEvent(soundKey, out EventReference evt)) return; // нет события → тишина
            RuntimeManager.PlayOneShot(evt);
        }

        public void Stop(string soundKey)
        {
            // Затычки — fire-and-forget one-shot'ы, хендл не держим (нечего останавливать).
            // Loop/музыка получат хранимые EventInstance в отдельной итерации.
        }

        public void SetMasterVolume(float volume) => SetBusVolume("bus:/", volume);
        public void SetMusicVolume(float volume)  => SetBusVolume("bus:/Music", volume);
        public void SetSfxVolume(float volume)    => SetBusVolume("bus:/SFX", volume);

        // Шина может быть невалидна, пока не загружен соответствующий банк — тогда тихо выходим (безопасно
        // для пустого проекта; bus:/SFX подхватится, когда Макс разведёт шины в FMOD Studio, кода менять не надо).
        private static void SetBusVolume(string busPath, float volume)
        {
            try
            {
                var bus = RuntimeManager.GetBus(busPath);
                if (bus.isValid()) bus.setVolume(Mathf.Clamp01(volume));
            }
            catch (BankLoadException) { }
            catch (System.Exception) { }
        }

        public void SetGlobalParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            // Если параметра/банка ещё нет — FMOD вернёт EVENT_NOTFOUND без исключения, спама нет.
            RuntimeManager.StudioSystem.setParameterByName(name, value);
        }
    }
}
