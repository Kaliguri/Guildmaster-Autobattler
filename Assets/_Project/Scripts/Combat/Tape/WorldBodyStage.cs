using System.Collections.Generic;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Тела на арене вне боя: кто стоит во дворе гильдии, на Ристалище и в строю между забегами.
    /// </summary>
    /// <remarks>
    /// <b>Это не симуляция и не её замена.</b> Здесь нет ни тика, ни урона, ни времени — только
    /// список того, кто где стоит. Пока боевая симуляция жила вечно, эту роль исполняла она,
    /// замороженная паузой; из-за этого у боя не было границы в жизненном цикле, и границу
    /// объявляли вручную (<c>ResetBattle</c>, пересев RNG, ручные сбросы ленты). Теперь симуляция
    /// принадлежит бою, а тела вне боя — миру.
    /// <para><b>Доля кадра постоянна</b> и равна единице: интерполировать нечего, у стоящего тела
    /// прошлая позиция совпадает с текущей. Ноль дал бы тот же результат, но единица честнее читается
    /// как «показан конец шага», а не «начало движения, которого нет».</para>
    /// </remarks>
    public sealed class WorldBodyStage : IStageFrameSource
    {
        private static readonly IReadOnlyList<ProjectileSnapshot> NoProjectiles =
            new List<ProjectileSnapshot>(0);

        private readonly List<UnitSnapshot> _bodies = new List<UnitSnapshot>(16);

        /// <summary>Сколько тел стоит на арене.</summary>
        public int Count => _bodies.Count;

        public float Alpha => 1f;

        /// <summary>Время сцену не двигает: тела стоят, пока их не переставят.</summary>
        public void Advance(float deltaTime) { }

        /// <summary>Заменить состав целиком. Пустой список — арена очищается.</summary>
        public void Set(IReadOnlyList<UnitSnapshot> bodies)
        {
            _bodies.Clear();
            if (bodies == null) return;

            for (int i = 0; i < bodies.Count; i++) _bodies.Add(bodies[i]);
        }

        /// <summary>Убрать всех: выход в бой, выход в меню.</summary>
        public void Clear() => _bodies.Clear();

        public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units,
                                out IReadOnlyList<ProjectileSnapshot> projectiles)
        {
            projectiles = NoProjectiles;

            if (_bodies.Count == 0)
            {
                units = null;
                return false;
            }

            units = _bodies;
            return true;
        }
    }
}
