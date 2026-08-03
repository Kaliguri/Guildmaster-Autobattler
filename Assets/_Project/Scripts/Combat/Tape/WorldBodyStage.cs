using System.Collections.Generic;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Одно тело на арене вне боя: где стоит (<see cref="Body"/>) и кто это (<see cref="Who"/>).
    /// </summary>
    /// <remarks>
    /// Пара, а не два списка рядом: паспорт и снимок ищутся по одному и тому же id, и разъехаться
    /// они могут только если их кто-то разложил по разным местам. Здесь такого места нет.
    /// </remarks>
    public readonly struct WorldBody
    {
        public readonly UnitSnapshot Body;
        public readonly UnitIdentity Who;

        public WorldBody(in UnitSnapshot body, in UnitIdentity who)
        {
            Body = body;
            Who  = who;
        }
    }

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
    /// <para><b>Держит и паспорта</b> (<see cref="IUnitDirectory"/>): тела мира выходят на арену
    /// мимо симуляции, а событие спавна — единственное, чем паспорта наполнялись раньше. Без этого
    /// показ получил бы кадр с телами, о которых не знает, кого рисовать.</para>
    /// </remarks>
    public sealed class WorldBodyStage : IStageFrameSource, IUnitDirectory
    {
        private static readonly IReadOnlyList<ProjectileSnapshot> NoProjectiles =
            new List<ProjectileSnapshot>(0);

        private readonly List<UnitSnapshot> _bodies = new List<UnitSnapshot>(16);
        private readonly Dictionary<int, UnitIdentity> _who = new Dictionary<int, UnitIdentity>(16);

        /// <summary>Сколько тел стоит на арене — и, ровно столько же, паспортов у них.</summary>
        public int Count => _bodies.Count;

        public float Alpha => 1f;

        /// <summary>Время сцену не двигает: тела стоят, пока их не переставят.</summary>
        public void Advance(float deltaTime) { }

        /// <summary>Заменить состав целиком. Пустой список — арена очищается.</summary>
        public void Set(IReadOnlyList<WorldBody> bodies)
        {
            _bodies.Clear();
            _who.Clear();
            if (bodies == null) return;

            for (int i = 0; i < bodies.Count; i++)
            {
                WorldBody b = bodies[i];
                _bodies.Add(b.Body);
                _who[b.Body.Id] = b.Who;
            }
        }

        /// <summary>Убрать всех: выход в бой, выход в меню.</summary>
        public void Clear()
        {
            _bodies.Clear();
            _who.Clear();
        }

        public bool TryGet(int unitId, out UnitIdentity identity) => _who.TryGetValue(unitId, out identity);

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
