using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Один порез на теле: место, направление, длина и запас — сколько HP снял породивший его удар.
    /// </summary>
    /// <remarks>
    /// <b>Порез — это СОСТОЯНИЕ тела, а не событие.</b> Он живёт не в пуле эффектов, а рядом с тинтом и
    /// вспышкой, потому что остаётся на теле до заживления. Красное у нас — не брызги, а ВСКРЫТОЕ: тело
    /// помнит бой, и сильно раненый юнит выглядит изрубленным без единой цифры.
    /// <para>
    /// Место хранится в ЛОКАЛЬНЫХ координатах своей части, а не в мировых: предплечье двигается каждый
    /// кадр, и рана на нём обязана ехать вместе с ним, а не висеть в воздухе там, где её нанесли.
    /// </para>
    /// </remarks>
    public readonly struct BodyCut
    {
        /// <summary>Индекс части тела, на которой лежит порез (он же бит в <see cref="PartMask"/>).</summary>
        public readonly int PartIndex;

        /// <summary>Место в локальных координатах части.</summary>
        public readonly Vector2 Local;

        /// <summary>Направление пореза в пространстве части, радианы — тот же вектор, что у формы удара.</summary>
        public readonly float Angle;

        /// <summary>Длина, локальные единицы части.</summary>
        public readonly float Length;

        /// <summary>Сколько HP снял удар, оставивший порез. По этому запасу порез и заживает.</summary>
        public readonly float Budget;

        /// <summary>Сколько запаса осталось: <c>Remaining / Budget</c> — это яркость пореза.</summary>
        public readonly float Remaining;

        public BodyCut(int partIndex, Vector2 local, float angle, float length, float budget, float remaining)
        {
            PartIndex = partIndex;
            Local     = local;
            Angle     = angle;
            Length    = length;
            Budget    = budget;
            Remaining = remaining;
        }

        /// <summary>Яркость 0..1 — доля незажившего запаса.</summary>
        public float Brightness => Budget > 1e-4f ? Mathf.Clamp01(Remaining / Budget) : 0f;

        /// <summary>Порез, у которого запас уменьшен на <paramref name="healed"/>.</summary>
        public BodyCut Healed(float healed) =>
            new BodyCut(PartIndex, Local, Angle, Length, Budget, Mathf.Max(0f, Remaining - healed));
    }

    /// <summary>
    /// Журнал ран одного тела: порезы в порядке нанесения, лимит и заживление.
    /// </summary>
    /// <remarks>
    /// <b>Хил заживляет САМЫЕ СТАРЫЕ.</b> Тело чинится в том порядке, в каком его ломали, поэтому
    /// исцеление читается как процесс, а не как мгновенный сброс. И гасит хил пропорционально запасу:
    /// порез на тысячу при лечении на четыреста тускнеет на сорок процентов — мерцания нет, потому что
    /// порез не выключается, а угасает.
    /// <para>
    /// <b>Лимит 12 — верх диапазона Макса (8–12).</b> Порезы очень мелкие, поэтому дюжина читается как
    /// «изрублен», а не как шум; сверх лимита вытесняются старые, что совпадает с логикой заживления и
    /// потому не выглядит обрезкой.
    /// </para>
    /// </remarks>
    public sealed class BodyCutLedger
    {
        /// <summary>Сколько порезов держит тело.</summary>
        public const int Limit = 12;

        private readonly List<BodyCut> _cuts = new List<BodyCut>(Limit);

        /// <summary>Порезы в порядке нанесения: первый — самый старый.</summary>
        public IReadOnlyList<BodyCut> Cuts => _cuts;

        /// <summary>Есть ли на теле хоть одна рана.</summary>
        public bool HasCuts => _cuts.Count > 0;

        /// <summary>Записать порез. Сверх лимита вытесняется самый старый.</summary>
        public void Add(in BodyCut cut)
        {
            if (cut.Budget <= 0f) return;   // удар без урона тела не вскрывал

            _cuts.Add(cut);
            if (_cuts.Count > Limit) _cuts.RemoveAt(0);
        }

        /// <summary>
        /// Залечить на <paramref name="amount"/> HP, начиная с самых старых ран. Зажившие пропадают.
        /// </summary>
        /// <returns><c>true</c>, если состояние тела изменилось и его надо переписать в шейдер.</returns>
        public bool Heal(float amount)
        {
            if (amount <= 0f || _cuts.Count == 0) return false;

            float left = amount;
            bool changed = false;

            for (int i = 0; i < _cuts.Count && left > 0f; i++)
            {
                BodyCut cut = _cuts[i];
                if (cut.Remaining <= 0f) continue;

                float take = Mathf.Min(left, cut.Remaining);
                _cuts[i] = cut.Healed(take);
                left -= take;
                changed = true;
            }

            // Убираем догоревшие с головы очереди: они и заживали первыми, значит и уходят первыми.
            while (_cuts.Count > 0 && _cuts[0].Remaining <= 1e-4f)
                _cuts.RemoveAt(0);

            return changed;
        }

        /// <summary>Стереть все раны — вид переиспользуется под нового юнита, и чужие порезы ему не положены.</summary>
        public void Clear() => _cuts.Clear();
    }
}
