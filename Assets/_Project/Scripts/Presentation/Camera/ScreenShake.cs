using Unity.Cinemachine;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Тряска камеры как Cinemachine-extension: добавляет затухающее смещение + крен (roll) к финальному
    /// состоянию камеры (стадия Finalize). Именно extension, а не правка transform — чтобы корректно ложиться
    /// поверх Follow/Brain во всех режимах камеры. Вращение обязательно — урок Vlambeer: чистый сдвиг читается
    /// как глюк, доля градуса крена — как удар. Затухание на unscaled-времени: тряска резкая и не растягивается
    /// в cinematic slowmo. Ставится на каждую vcam (кодом из <see cref="CameraModeController"/>), Shake — на все.
    /// </summary>
    public sealed class ScreenShake : CinemachineExtension
    {
        // Форма тряски — из CombatFeelConfig (ApplyConfig от CameraModeController), и только оттуда.
        // Своих чисел здесь нет: конфиг регистрируется в боевом скоупе с фолбэком на пустой инстанс, то есть
        // никогда не приходит null, а вторая копия значений просто разъезжалась бы с ассетом молча
        // (аудит 2026-07-26, R1-34/T-9). Нет конфига — нет и тряски, а не тряска «примерно такая».
        private Design.CombatFeelConfig _feel;
        private float _amplitude;
        private float _seed;

        /// <summary>Подать design-конфиг тряски.</summary>
        public void ApplyConfig(Design.CombatFeelConfig feel) => _feel = feel;

        /// <summary>Снять остаточную амплитуду мгновенно (перезапуск боя).</summary>
        public void ResetShake() => _amplitude = 0f;

        /// <summary>Тряхнуть: intensity 0..1. Берётся максимум с текущей амплитудой (удары не гасят друг друга).</summary>
        public void Shake(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= _amplitude) return;
            _amplitude = intensity;
            _seed = Random.value * 100f; // разные тряски — разный фазовый сдвиг шума (презентация, не сим)
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || _amplitude <= 0.0001f || _feel == null) return;

            float positionFraction = _feel.ShakePositionFraction;
            float rotationStrength = _feel.ShakeRotationStrength;
            float frequency        = _feel.ShakeFrequency;
            float decayPerSec      = _feel.ShakeDecayPerSec;

            // Линейная амплитуда (без квадрата — тот втрое резал силу). Сдвиг привязан к обзору камеры
            // (orthoSize), иначе на широком зуме абсолютные ед. незаметны.
            float amp   = _amplitude;
            float ortho = Mathf.Max(state.Lens.OrthographicSize, 1f);
            float t = Time.unscaledTime * frequency;
            float nx = (Mathf.PerlinNoise(_seed,        t) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(_seed + 19f,  t) - 0.5f) * 2f;
            float nr = (Mathf.PerlinNoise(_seed + 43f,  t) - 0.5f) * 2f;

            state.PositionCorrection    += new Vector3(nx, ny, 0f) * (positionFraction * ortho * amp);
            state.OrientationCorrection  = state.OrientationCorrection * Quaternion.Euler(0f, 0f, nr * rotationStrength * amp);

            _amplitude = Mathf.Max(0f, _amplitude - decayPerSec * Time.unscaledDeltaTime);
        }
    }
}
