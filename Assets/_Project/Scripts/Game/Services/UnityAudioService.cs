using Guildmaster.Core.Audio;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Заглушка <see cref="IAudioService"/> на Unity Audio. Фаза 1 — только Debug.Log.
    /// FMOD-реализация подключается в Фазе 9 без изменения зависимостей.
    /// </summary>
    public sealed class UnityAudioService : IAudioService
    {
        public void Play(string soundKey)
        {
            Debug.Log($"[UnityAudioService] - Play: {soundKey}");
        }

        public void PlayAt(string soundKey, UnityEngine.Vector3 position)
        {
            Debug.Log($"[UnityAudioService] - PlayAt: {soundKey} @ {position}");
        }

        public void Stop(string soundKey)
        {
            Debug.Log($"[UnityAudioService] - Stop: {soundKey}");
        }

        public void StopAll()
        {
            Debug.Log("[UnityAudioService] - StopAll");
        }

        public void SetMasterVolume(float volume) { }

        public void SetMusicVolume(float volume) { }

        public void SetSfxVolume(float volume) { }

        // Заглушка: параметр пишется каждый кадр (slowmo) — молча глотаем, без лог-спама.
        public void SetGlobalParameter(string name, float value) { }
    }
}
