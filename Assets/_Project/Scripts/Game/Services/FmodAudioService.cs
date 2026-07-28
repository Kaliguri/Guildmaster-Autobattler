using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using Guildmaster.Core.Audio;
using UnityEngine;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// FMOD-реализация <see cref="IAudioService"/> (вики impl «09» §П3). Резолвнутый ключ от
    /// <see cref="Guildmaster.Presentation.Audio.AudioResolver"/> → <see cref="EventReference"/> через
    /// <see cref="Guildmaster.Presentation.Audio.AudioCatalog"/> → воспроизведение. Пустой
    /// <see cref="EventReference"/> (нет события/банка) — тихо пропускаем, без ошибок FMOD, поэтому пустой
    /// каталог безопасен. Игровая логика FMOD напрямую не трогает — только через этот класс (правило CLAUDE.md).
    ///
    /// One-shot (удары, UI, стингеры) летят через <c>PlayOneShot</c> и забываются. Длящиеся события
    /// (музыка, амбиент — у них снят флаг one-shot в Studio) получают хранимый <see cref="EventInstance"/>:
    /// иначе их нечем остановить и повторный Play плодил бы наложение петель.
    /// </summary>
    public sealed class FmodAudioService : IAudioService
    {
        private readonly Guildmaster.Presentation.Audio.AudioCatalog _catalog;

        // Живые петли по ключу. One-shot сюда не попадают — их хендл не нужен.
        private readonly Dictionary<string, EventInstance> _loops = new Dictionary<string, EventInstance>();

        public FmodAudioService(Guildmaster.Presentation.Audio.AudioCatalog catalog) => _catalog = catalog;

        public void Play(string soundKey) => PlayInternal(soundKey, Vector3.zero);

        public void PlayAt(string soundKey, Vector3 position) => PlayInternal(soundKey, position);

        private void PlayInternal(string soundKey, Vector3 position)
        {
            if (_catalog == null || string.IsNullOrEmpty(soundKey)) return;
            if (!_catalog.TryGetEvent(soundKey, out EventReference evt)) return; // нет события → тишина

            if (!TryGetDescription(evt, out EventDescription description))
            {
                RuntimeManager.PlayOneShot(evt, position); // банк ещё грузится — считаем one-shot'ом
                return;
            }

            if (description.isOneshot(out bool oneShot) != FMOD.RESULT.OK || oneShot)
            {
                // Позиция важна только для событий со спатиалайзером (боевые); остальные её игнорируют.
                RuntimeManager.PlayOneShot(evt, position);
                return;
            }

            // Петля: повторный Play того же ключа — no-op, а не второй слой поверх играющего.
            if (_loops.TryGetValue(soundKey, out EventInstance existing) && IsAlive(existing)) return;

            EventInstance instance = RuntimeManager.CreateInstance(evt);
            if (!instance.isValid()) return;
            instance.start();
            _loops[soundKey] = instance;
        }

        public void Stop(string soundKey)
        {
            if (string.IsNullOrEmpty(soundKey)) return;
            if (!_loops.TryGetValue(soundKey, out EventInstance instance)) return;
            _loops.Remove(soundKey);
            if (!instance.isValid()) return;
            instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); // AHDSR/фейд события отрабатывает — обрыв слышен
            instance.release();
        }

        /// <summary>Погасить все петли (смена сцены, выход в меню, конец забега).</summary>
        public void StopAll()
        {
            foreach (KeyValuePair<string, EventInstance> pair in _loops)
            {
                if (!pair.Value.isValid()) continue;
                pair.Value.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                pair.Value.release();
            }
            _loops.Clear();
        }

        public void SetMasterVolume(float volume) => SetBusVolume("bus:/", volume);
        public void SetMusicVolume(float volume)  => SetBusVolume("bus:/Music", volume);
        public void SetSfxVolume(float volume)    => SetBusVolume("bus:/SFX", volume);

        // Шина может быть невалидна, пока не загружен соответствующий банк — тогда тихо выходим (безопасно
        // для пустого проекта; шины появляются вместе с банками, кода менять не надо).
        private static void SetBusVolume(string busPath, float volume)
        {
            try
            {
                var bus = RuntimeManager.GetBus(busPath);
                if (bus.isValid()) bus.setVolume(Mathf.Clamp01(volume));
            }
            // Банк ещё не загружен — ожидаемо и молчаливо: шины появляются вместе с банками.
            catch (BankLoadException) { }
            // Всё остальное ожидаемым не является. Прежний голый catch превращал любой отказ FMOD в тишину
            // без единой записи в логе — искать причину было негде (аудит фолбэков 2026-07-26, п.10).
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FmodAudioService] - не удалось выставить громкость шины '{busPath}': {e.Message}");
            }
        }

        public void SetGlobalParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            // Если параметра/банка ещё нет — FMOD вернёт EVENT_NOTFOUND без исключения, спама нет.
            RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        private static bool TryGetDescription(EventReference evt, out EventDescription description)
        {
            description = default;
            try
            {
                description = RuntimeManager.GetEventDescription(evt);
                return description.isValid();
            }
            catch (System.Exception e)
            {
                // Событие или его банк недоступны. Сообщаем ОДИН раз на ссылку: метод зовётся на каждый
                // звук, и лог на каждый вызов утонул бы в спаме — но полное молчание прятало бы пустой
                // каталог целиком (аудит фолбэков 2026-07-26, п.10).
                string id = evt.IsNull ? "(пустая ссылка)" : evt.Guid.ToString();
                if (_reportedBrokenEvents.Add(id))
                    Debug.LogWarning($"[FmodAudioService] - событие {id} недоступно, звук пропущен: {e.Message}");
                return false;
            }
        }

        // Ссылки, о которых уже сказали. Только для дедупа лога — на звук не влияет.
        private static readonly HashSet<string> _reportedBrokenEvents = new HashSet<string>();

        private static bool IsAlive(EventInstance instance)
        {
            if (!instance.isValid()) return false;
            return instance.getPlaybackState(out PLAYBACK_STATE state) == FMOD.RESULT.OK
                   && state != PLAYBACK_STATE.STOPPED;
        }
    }
}
