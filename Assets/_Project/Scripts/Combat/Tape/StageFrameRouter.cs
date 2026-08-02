using System;
using System.Collections.Generic;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Единственный вход показа за кадром сцены: пока идёт бой — лента, всё остальное время — тела
    /// мира.
    /// </summary>
    /// <remarks>
    /// <b>Почему роутер, а не «презентер сам решает».</b> Решать пришлось бы каждому потребителю
    /// кадра, и они разошлись бы: одному видно бой, другому уже мир. Владелец факта «что сейчас на
    /// арене» ровно один, и это он.
    /// <para><b>Бой подключается сам и сам отключается.</b> Мир про бой не знает и знать не должен:
    /// боевой скоуп при рождении зовёт <see cref="Bind"/>, при смерти — <see cref="Unbind"/>, и
    /// показ возвращается к телам мира. Отвязка проверяет, что отвязывают именно текущий источник:
    /// иначе умирающий скоуп мог бы сбросить чужую привязку и погасить арену следующего боя.</para>
    /// </remarks>
    public sealed class StageFrameRouter : IStageFrameSource
    {
        private readonly WorldBodyStage _world;

        private IStageFrameSource _battle;

        public StageFrameRouter(WorldBodyStage world)
            => _world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>Кто сейчас поставляет кадр.</summary>
        public IStageFrameSource Active => _battle ?? (IStageFrameSource)_world;

        /// <summary>Идёт ли показ боя (в противовес статичной сцене мира).</summary>
        public bool ShowingBattle => _battle != null;

        public float Alpha => Active.Alpha;

        public void Advance(float deltaTime) => Active.Advance(deltaTime);

        public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units,
                                out IReadOnlyList<ProjectileSnapshot> projectiles)
            => Active.TryGetFrame(out units, out projectiles);

        /// <summary>Подключить источник боя. Зовёт боевой скоуп при рождении.</summary>
        public void Bind(IStageFrameSource battle)
            => _battle = battle ?? throw new ArgumentNullException(nameof(battle));

        /// <summary>
        /// Отключить источник боя и вернуться к телам мира. Чужую привязку не трогает — умерший бой
        /// не должен гасить арену того, кто уже начался.
        /// </summary>
        public void Unbind(IStageFrameSource battle)
        {
            if (!ReferenceEquals(_battle, battle)) return;
            _battle = null;
        }
    }
}
