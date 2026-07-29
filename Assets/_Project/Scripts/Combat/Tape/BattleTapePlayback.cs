using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using UnityEngine;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Момент показа: сколько боя игрок уже увидел. Ползёт по ленте в реальном времени и никогда не
    /// обгоняет фронт симуляции — а отстаёт от него настолько, насколько сим успел уйти вперёд.
    /// Именно это отставание и даёт право знать будущее (телеграфы, slowmo, подводки камеры).
    /// <para><b>Кто отвечает за величину лага:</b> НЕ этот класс. Показ просто идёт в реальном
    /// времени; запас перед ним держит продюсер (<c>CombatLoopService</c>), разгоняя сим до
    /// <see cref="LookaheadTicks"/>. Здесь ожидания окна нет намеренно: пока идёт мир или расстановка,
    /// сим не тикает вообще, и «ждущий окно» показ означал бы пустую арену.</para>
    /// <para><b>Скорости показа нет и не планируется</b> (решение Макса 2026-07-29): фаст-форвард
    /// означал бы, что бой скучно смотреть, и вдобавок ломал бы всю режиссуру, рассчитанную на
    /// нормальный темп. Пауза — есть, она приходит в Ф4.</para>
    /// </summary>
    public sealed class BattleTapePlayback
    {
        /// <summary>
        /// Целевой запас сима перед показом: 10 секунд (решение Макса 2026-07-29). Держит его продюсер;
        /// показу это число нужно только чтобы сказать, добрал ли запас (<see cref="HasFullLead"/>).
        /// </summary>
        public const int LookaheadTicks = 10 * SimConstants.TickRate;

        private readonly BattleTape _tape;

        private float _accumulator;
        private int   _viewTick = BattleTape.NoTick;

        public BattleTapePlayback(BattleTape tape)
        {
            _tape = tape;
        }

        /// <summary>Тик, который показан сейчас. <see cref="BattleTape.NoTick"/> — показ ещё не начался.</summary>
        public int ViewTick => _viewTick;

        /// <summary>Идёт ли показ: окно набрано и кадры есть.</summary>
        public bool IsPlaying => _viewTick != BattleTape.NoTick;

        /// <summary>
        /// Доля кадра, накопленная сверх показанного тика — из неё показ интерполирует движение между
        /// снимками. Ровно та же роль, что у доли боевого луча, но отсчитывается от момента ПОКАЗА.
        /// </summary>
        public float Alpha => Mathf.Clamp01(_accumulator / SimConstants.TickDelta);

        /// <summary>Набран ли полный запас: пока нет — телеграфам и подводкам не на что опираться.</summary>
        public bool HasFullLead => Lead >= LookaheadTicks;

        /// <summary>Сколько тиков сим держит в запасе перед показом. Меньше нуля не бывает.</summary>
        public int Lead => _tape.FrontTick == BattleTape.NoTick || _viewTick == BattleTape.NoTick
            ? 0
            : Mathf.Max(0, _tape.FrontTick - _viewTick);

        /// <summary>
        /// Продвинуть показ на прошедшее время. Зовётся ровно раз за кадр — владелец момента показа
        /// один, иначе разные потребители увидели бы разные «сейчас» в одном кадре.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (_viewTick == BattleTape.NoTick)
            {
                // Первый кадр показа — самый свежий, какой есть. Отставание накопится само, когда
                // продюсер начнёт разгонять сим; до боя (мир, расстановка) его и не должно быть:
                // там игрок двигает юнитов сам и обязан видеть их сразу.
                if (_tape.FrontTick == BattleTape.NoTick) return;

                _viewTick    = _tape.FrontTick;
                _accumulator = 0f;
                return;
            }

            _accumulator += deltaTime;
            while (_accumulator >= SimConstants.TickDelta)
            {
                // Обогнать сим нельзя: показ упирается во фронт ленты и ждёт там. В обычном бою этого
                // не случается — сим уходит вперёд быстрее реального времени.
                if (_viewTick >= _tape.FrontTick)
                {
                    _accumulator = 0f;
                    break;
                }

                _viewTick++;
                _accumulator -= SimConstants.TickDelta;
            }
        }

        /// <summary>Кадр показываемого тика. <c>false</c> — показ ещё не начался или кадр вытеснен.</summary>
        public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units)
        {
            if (_viewTick == BattleTape.NoTick)
            {
                units = null;
                return false;
            }
            return _tape.TryGetFrame(_viewTick, out units);
        }

        /// <summary>
        /// Заглянуть вперёд на <paramref name="ticksAhead"/> от момента показа — то самое знание
        /// будущего, за которое куплен лаг. Возвращает <c>false</c>, если сим ещё не досчитал так далеко.
        /// </summary>
        public bool TryGetFrameAhead(int ticksAhead, out IReadOnlyList<UnitSnapshot> units)
        {
            if (_viewTick == BattleTape.NoTick)
            {
                units = null;
                return false;
            }
            return _tape.TryGetFrame(_viewTick + ticksAhead, out units);
        }

        /// <summary>Сбросить момент показа (dev-рестарт боя): показ снова ждёт набора окна.</summary>
        public void Reset()
        {
            _viewTick    = BattleTape.NoTick;
            _accumulator = 0f;
        }
    }
}
