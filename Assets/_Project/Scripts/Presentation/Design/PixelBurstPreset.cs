using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Параметры одного пиксельного «брызга» (<see cref="Guildmaster.Presentation.PixelBurst"/>): цвет, кол-во
    /// частиц, дальность разлёта, размер пикселя, время жизни, гравитация и угол конуса. Задаются в
    /// <see cref="CombatFeelConfig"/> на каждый тип VFX (hit-spark / muzzle / dust / heal), крутятся дизайнером.
    /// </summary>
    [System.Serializable]
    public sealed class PixelBurstPreset
    {
        [Tooltip("Цвет частиц.")]
        public Color Color = Color.white;
        [Tooltip("Число частиц (максимум — 32; сколько реально показать).")]
        [Range(1, 32)] public int Count = 12;
        [Tooltip("Дальность разлёта за жизнь, мировые единицы.")]
        public float Speed = 1.2f;
        [Tooltip("Размер пикселя-частицы, мировые единицы.")]
        public float Size = 0.06f;
        [Tooltip("Время жизни, сек.")]
        public float Life = 0.4f;
        [Tooltip("Гравитация (тянет вниз к концу), мировые ед.")]
        public float Gravity = 0f;
        [Tooltip("Угол разлёта: 360 = во все стороны, узкий = направленный (напр. дуло/хил вверх).")]
        [Range(0f, 360f)] public float SpreadDeg = 360f;
    }
}
