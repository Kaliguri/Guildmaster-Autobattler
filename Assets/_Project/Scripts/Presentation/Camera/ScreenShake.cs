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
        [Tooltip("Смещение как ДОЛЯ обзора камеры (orthoSize) при интенсивности 1 — не зависит от зума. " +
                 "0.08 = ±8% полувысоты кадра.")]
        [SerializeField] private float _positionFraction = 0.08f;
        [Tooltip("Крен (roll) в градусах при интенсивности 1.")]
        [SerializeField] private float _rotationStrength = 4f;
        [Tooltip("Частота дрожания (Гц-подобная).")]
        [SerializeField] private float _frequency = 26f;
        [Tooltip("Скорость затухания амплитуды в секунду (unscaled). Меньше = дольше трясёт.")]
        [SerializeField] private float _decayPerSec = 2f;

        private float _amplitude;
        private float _seed;

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
            if (stage != CinemachineCore.Stage.Finalize || _amplitude <= 0.0001f) return;

            // Линейная амплитуда (без квадрата — тот втрое резал силу). Сдвиг привязан к обзору камеры
            // (orthoSize), иначе на широком зуме абсолютные ед. незаметны.
            float amp   = _amplitude;
            float ortho = Mathf.Max(state.Lens.OrthographicSize, 1f);
            float t = Time.unscaledTime * _frequency;
            float nx = (Mathf.PerlinNoise(_seed,        t) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(_seed + 19f,  t) - 0.5f) * 2f;
            float nr = (Mathf.PerlinNoise(_seed + 43f,  t) - 0.5f) * 2f;

            state.PositionCorrection    += new Vector3(nx, ny, 0f) * (_positionFraction * ortho * amp);
            state.OrientationCorrection  = state.OrientationCorrection * Quaternion.Euler(0f, 0f, nr * _rotationStrength * amp);

            _amplitude = Mathf.Max(0f, _amplitude - _decayPerSec * Time.unscaledDeltaTime);
        }
    }
}
