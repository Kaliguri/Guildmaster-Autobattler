using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Расклад Ристалища по умолчанию — ОБЕ команды, которые встают на площадку, когда забега нет
    /// (ГДД «Modes - Proving Grounds»). Единственный экземпляр на проект.
    /// </summary>
    /// <remarks>
    /// Обе стороны заданы китами, а не энкаунтером: на полигоне противник — такие же бойцы, которых
    /// игрок разглядывает, а не авторенный состав врагов. Отдельный ассет, а не список в коде: сегодня
    /// расклад читается отсюда, а когда появится экран сборки, он будет заполнять тот же контракт —
    /// сменится источник данных, не поток входа.
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Proving Grounds Config", fileName = "ProvingGroundsConfig")]
    public sealed class ProvingGroundsConfig : ScriptableObject
    {
        [Tooltip("Отряд игрока (левая сторона). Пусто = вход на площадку вне забега невозможен.")]
        [SerializeField] private RelicData[] _squad = new RelicData[0];

        [Tooltip("Противник (правая сторона). Пусто = отряд стоит один, драться не с кем.")]
        [SerializeField] private RelicData[] _opponents = new RelicData[0];

        [Tooltip("Глубина строя: насколько команда отступает от центра арены.")]
        [SerializeField] private float _lineX = 3f;

        [Tooltip("Шаг между бойцами по вертикали.")]
        [SerializeField] private float _spacing = 1.2f;

        public int SquadCount => _squad != null ? _squad.Length : 0;
        public int OpponentCount => _opponents != null ? _opponents.Length : 0;

        public RelicData SquadAt(int index) => _squad[index];
        public RelicData OpponentAt(int index) => _opponents[index];

        /// <summary>Позиция бойца отряда: колонна слева, симметричная относительно оси арены.</summary>
        public Vector2 SquadPositionAt(int index) => PositionAt(index, SquadCount, -1f);

        /// <summary>Позиция противника: та же колонна, отражённая по X.</summary>
        public Vector2 OpponentPositionAt(int index) => PositionAt(index, OpponentCount, 1f);

        private Vector2 PositionAt(int index, int count, float side)
            => new Vector2(side * _lineX, (index - (count - 1) * 0.5f) * _spacing);
    }
}
