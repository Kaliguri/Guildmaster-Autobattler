using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Список боёв, которые идут ЗА главным меню, и правила их показа.
    /// </summary>
    /// <remarks>
    /// <b>Зачем это данные, а не код.</b> Бой за меню — контент: «две реликвии против кучи гоблинов, всё
    /// прям на грани» (Макс, 04.08.2026). Набор таких боёв правится без единой строки C#, и заказ на
    /// двадцать штук — это двадцать строк здесь, а не двадцать веток в коде.
    ///
    /// <para><b>Записи боёв не будет и не нужно.</b> Ядро детерминировано: пресет плюс сид дают один и
    /// тот же бой покадрово, поэтому «заранее просчитанный бой» получается бесплатно, а система реплеев
    /// ради фона не заводится. Сид на запись задаётся здесь же — им и подбирается тот прогон, который
    /// понравился.</para>
    ///
    /// <para><b>Про баланс договорились заранее:</b> когда числа поедут, «эпик» боёв поедет вместе с
    /// ними, и это принято («Пока забьем, думаю? Даже если поедет "Эпик" боя, пока нам простителньо»).
    /// У фонового боя нет условия успеха, кроме «идёт и выглядит живым»: победа любой стороны одинаково
    /// годится. Единственный настоящий отказ — бой, который кончился мгновенно или не кончается вовсе,
    /// и его закрывает <see cref="MaxSeconds"/>.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "MenuBattleConfig", menuName = "Alebardium/Configs/Menu Battle Config")]
    public sealed class MenuBattleConfig : ScriptableObject
    {
        /// <summary>
        /// Одна дуэль: кто с кем. Стороны задаются реликвиями напрямую — <c>RelicData</c> наследует
        /// <c>UnitData</c>, то есть реликвия и есть боец, и разворачивать её во что-то ещё не нужно.
        /// </summary>
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("Левая сторона (команда 0). Обычно двое.")]
            [SerializeField] private RelicData[] _squad = new RelicData[0];

            [Tooltip("Правая сторона (команда 1). Обычно двое.")]
            [SerializeField] private RelicData[] _opponents = new RelicData[0];

            [Tooltip("Сид прогона. Ноль — вывести сид из состава: одна и та же дуэль пойдёт одинаково.")]
            [SerializeField] private ulong _seed;

            public RelicData[] Squad => _squad;
            public RelicData[] Opponents => _opponents;
            public ulong Seed => _seed;

            /// <summary>Есть ли кому драться: пустая сторона даёт бой, который кончается мгновенно.</summary>
            public bool IsPlayable =>
                _squad != null && _opponents != null && _squad.Length > 0 && _opponents.Length > 0;
        }

        [Tooltip("Дуэли, которые крутятся за меню. Берутся вразнобой, следующая — при новом входе в меню.")]
        [SerializeField] private List<Entry> _battles = new();

        [Tooltip("Пресет-носитель боя: арена и режим расстановки. Состав приходит НЕ из него, а из дуэли — " +
                 "но без пресета бой остаётся на паузе расстановки и сам не начинается.")]
        [SerializeField] private BattlePresetData _carrierPreset;

        [Tooltip("Насколько стороны разведены по X от центра арены.")]
        [Min(1f)]
        [SerializeField] private float _lineX = 3f;

        [Tooltip("Шаг между бойцами одной стороны по Y.")]
        [Min(0.3f)]
        [SerializeField] private float _spacing = 1.2f;

        [Tooltip("Показывать бой за меню вообще. Выключено — за меню остаётся обычный задник.")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("Сколько секунд крутить один бой, если он не кончился сам. По истечении берётся следующий.")]
        [Min(5f)]
        [SerializeField] private float _maxSeconds = 75f;

        [Tooltip("Пауза перед сменой боя после его конца — чтобы добивание успело досмотреться.")]
        [Min(0f)]
        [SerializeField] private float _afterEndSeconds = 2.5f;

        public IReadOnlyList<Entry> Battles => _battles;
        public BattlePresetData CarrierPreset => _carrierPreset;
        public float LineX => _lineX;
        public float Spacing => _spacing;
        public bool Enabled => _enabled;
        public float MaxSeconds => _maxSeconds;
        public float AfterEndSeconds => _afterEndSeconds;

        /// <summary>Есть ли вообще что показывать: включено и хотя бы один заполненный пресет.</summary>
        public bool HasAnything
        {
            get
            {
                if (!_enabled || _carrierPreset == null) return false;
                for (int i = 0; i < _battles.Count; i++)
                    if (_battles[i] != null && _battles[i].IsPlayable) return true;
                return false;
            }
        }
    }
}
