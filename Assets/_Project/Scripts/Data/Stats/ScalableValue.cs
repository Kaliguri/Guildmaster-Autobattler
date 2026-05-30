using System;

namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Величина, масштабируемая от статов источника: <c>Base + Σ(reader.Get(term.Stat) × term.Ratio)</c>.
    /// Основа для урона/исцеления способностей (напр. «120 + 0.6×AbilityPower»). Авторится на SO.
    /// </summary>
    /// <remarks>Шов закладывается в Фазе 1; реальное применение в способностях — Фаза 2.</remarks>
    [Serializable]
    public struct ScalableValue
    {
        public float Base;
        public ScalingTerm[] Scalings;

        public ScalableValue(float @base, ScalingTerm[] scalings = null)
        {
            Base = @base;
            Scalings = scalings;
        }

        /// <summary>Разворачивает значение по статам читателя. <c>null</c>-читатель → только <see cref="Base"/>.</summary>
        public float Resolve(IStatReader reader)
        {
            float result = Base;

            if (reader != null && Scalings != null)
            {
                for (int i = 0; i < Scalings.Length; i++)
                {
                    result += reader.Get(Scalings[i].Stat) * Scalings[i].Ratio;
                }
            }

            return result;
        }
    }

    /// <summary>Одно слагаемое масштабирования: доля <see cref="Ratio"/> от стата <see cref="Stat"/>.</summary>
    [Serializable]
    public struct ScalingTerm
    {
        public StatType Stat;
        public float Ratio;

        public ScalingTerm(StatType stat, float ratio)
        {
            Stat = stat;
            Ratio = ratio;
        }
    }
}
