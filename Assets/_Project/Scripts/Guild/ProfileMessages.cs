using System;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Запрос показать экран профиля: слоты и идентичность (ник, цвет, курсор).
    /// </summary>
    /// <remarks>
    /// <b>Два повода показать его, и они различаются одним флагом.</b> Обычный — игрок сам открыл
    /// профиль из меню и волен уйти. Обязательный (<see cref="Required"/>) — профиля нет вовсе, и уйти
    /// некуда: забегу некуда писаться, а заводить слот молча за игрока мы перестали 03.08.2026.
    /// <para><see cref="OnClosed"/> зовётся ровно один раз — когда игрок закрыл экран или (в
    /// обязательном показе) когда профиль наконец появился.</para>
    /// </remarks>
    public readonly struct OpenProfileRequest
    {
        /// <summary>Уйти нельзя, пока профиль не выбран: «Назад» на экране не показывается вовсе.</summary>
        public readonly bool Required;

        /// <summary>Экран закрыт (ровно один вызов).</summary>
        public readonly Action OnClosed;

        public OpenProfileRequest(bool required, Action onClosed)
        {
            Required = required;
            OnClosed = onClosed;
        }
    }
}
