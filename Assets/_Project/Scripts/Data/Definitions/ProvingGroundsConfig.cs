using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Состав Ристалища по умолчанию — отряд, который встаёт на площадку, когда забега нет
    /// (ГДД «Modes - Proving Grounds»). Единственный экземпляр на проект.
    /// </summary>
    /// <remarks>
    /// Отдельный ассет, а не список в коде: сегодня состав читается отсюда, а когда появится экран
    /// сборки, он будет заполнять тот же контракт — сменится источник данных, не поток входа.
    /// Пустой список = площадка вне забега не открывается (и говорит об этом вслух).
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Proving Grounds Config", fileName = "ProvingGroundsConfig")]
    public sealed class ProvingGroundsConfig : ScriptableObject
    {
        [Tooltip("Кто стоит на площадке, когда забега нет. Пусто = вход вне забега невозможен.")]
        [SerializeField] private RelicData[] _squad = new RelicData[0];

        [Tooltip("Глубина строя: насколько отряд отступает от центра арены влево.")]
        [SerializeField] private float _lineX = 3f;

        [Tooltip("Шаг между бойцами по вертикали.")]
        [SerializeField] private float _spacing = 1.2f;

        public int Count => _squad != null ? _squad.Length : 0;

        public RelicData At(int index) => _squad[index];

        /// <summary>Позиция бойца в строю: колонна, симметричная относительно оси арены.</summary>
        public Vector2 PositionAt(int index)
            => new Vector2(-_lineX, (index - (Count - 1) * 0.5f) * _spacing);
    }
}
