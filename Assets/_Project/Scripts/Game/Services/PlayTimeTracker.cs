using VContainer.Unity;
using Guildmaster.Core.Persistence;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Секундомер наигранного: копит время и раз в секунду отдаёт его профилю.
    /// </summary>
    /// <remarks>
    /// <b>Считается всё время с запущенной игрой</b> (решение Макса 21.08.2026) — как считает Steam:
    /// меню, двор и пауза идут в зачёт наравне с боем. Вариант «только забеги» честнее отвечал бы на
    /// вопрос «сколько игралось», но разошёлся бы с числом в библиотеке Steam, а расхождение двух
    /// видимых игроку счётчиков объяснить нечем.
    ///
    /// <para><b>Время НЕмасштабированное.</b> Пауза боя и замедление меняют игровое время, а
    /// наигранное — про часы человека: с ускорением боя вдвое игрок не проводит за игрой вдвое
    /// больше.</para>
    ///
    /// <para><b>Отдаём целыми секундами, а не долями.</b> Хранится наигранное в секундах, и остаток
    /// копится здесь: сервис профиля сам решает, когда писать на диск (раз в минуту), поэтому частые
    /// мелкие вызовы ему ничего не стоят, а дробить единицу хранения незачем.</para>
    /// </remarks>
    public sealed class PlayTimeTracker : ITickable
    {
        private readonly IProfileService _profiles;

        /// <summary>Недосчитанный остаток секунды — переезжает в следующий кадр.</summary>
        private float _carry;

        public PlayTimeTracker(IProfileService profiles) => _profiles = profiles;

        public void Tick()
        {
            if (_profiles == null) return;

            _carry += UnityEngine.Time.unscaledDeltaTime;
            if (_carry < 1f) return;

            var whole = (long)_carry;
            _carry -= whole;

            // Профиля может не быть вовсе (первый запуск, экран выбора) — сервис это знает и молча
            // ничего не пишет. Спрашивать его об этом здесь значило бы завести второе мнение о том,
            // кто сейчас активен.
            _profiles.AddPlayedTime(whole);
        }
    }
}
