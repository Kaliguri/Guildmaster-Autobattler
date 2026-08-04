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
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("Пресет боя: состав, арена, режим расстановки.")]
            [SerializeField] private BattlePresetData _preset;

            [Tooltip("Сид прогона. Ноль — взять сид из пресета/по умолчанию.")]
            [SerializeField] private ulong _seed;

            public BattlePresetData Preset => _preset;
            public ulong Seed => _seed;
        }

        [Tooltip("Бои, которые крутятся за меню. Берутся вразнобой, следующий — при новом входе в меню.")]
        [SerializeField] private List<Entry> _battles = new();

        [Tooltip("Показывать бой за меню вообще. Выключено — за меню остаётся обычный задник.")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("Сколько секунд крутить один бой, если он не кончился сам. По истечении берётся следующий.")]
        [Min(5f)]
        [SerializeField] private float _maxSeconds = 75f;

        [Tooltip("Пауза перед сменой боя после его конца — чтобы добивание успело досмотреться.")]
        [Min(0f)]
        [SerializeField] private float _afterEndSeconds = 2.5f;

        public IReadOnlyList<Entry> Battles => _battles;
        public bool Enabled => _enabled;
        public float MaxSeconds => _maxSeconds;
        public float AfterEndSeconds => _afterEndSeconds;

        /// <summary>Есть ли вообще что показывать: включено и хотя бы один заполненный пресет.</summary>
        public bool HasAnything
        {
            get
            {
                if (!_enabled) return false;
                for (int i = 0; i < _battles.Count; i++)
                    if (_battles[i]?.Preset != null) return true;
                return false;
            }
        }
    }
}
