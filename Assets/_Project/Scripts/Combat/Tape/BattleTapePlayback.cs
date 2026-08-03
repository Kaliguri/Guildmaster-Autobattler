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
    public sealed class BattleTapePlayback : IStageFrameSource
    {
        /// <summary>
        /// Целевой запас сима перед показом: 10 секунд (решение Макса 2026-07-29). Держит его продюсер;
        /// показу это число нужно только чтобы сказать, добрал ли запас (<see cref="HasFullLead"/>).
        /// </summary>
        public const int LookaheadTicks = 10 * SimConstants.TickRate;

        private readonly BattleTape _tape;

        private float _accumulator;
        private int   _viewTick = BattleTape.NoTick;
        private int   _targetLead;

        public BattleTapePlayback(BattleTape tape)
        {
            _tape = tape;
        }

        /// <summary>
        /// Насколько показ ДОЛЖЕН отставать от фронта. <c>0</c> — идти вплотную.
        /// <para><b>Лаг — свойство БОЯ, а не показа вообще</b> (уточнено Максом 2026-07-29). Мир,
        /// карта, расстановка — реальное время: там игрок нажимает и обязан видеть результат сразу.
        /// Отставание включается только на время показа боя.</para>
        /// </summary>
        public int TargetLead => _targetLead;

        /// <summary>
        /// Задать требуемое отставание. Если показ отстал БОЛЬШЕ требуемого (вышли из боя в мир, или
        /// бой только что начался и лаг ещё не нужен) — показ подтягивается к фронту сразу: держать
        /// задержку там, где игрок взаимодействует, нельзя.
        /// </summary>
        public void SetTargetLead(int ticks)
        {
            _targetLead = ticks > 0 ? ticks : 0;

            if (_viewTick == BattleTape.NoTick || _tape.FrontTick == BattleTape.NoTick) return;
            // Отставание больше требуемого сокращаем; меньше — не добираем искусственно: запас
            // накапливается разгоном продюсера, а не прыжком показа назад в уже показанное.
            int maxViewTick = _tape.FrontTick - _targetLead;
            if (_viewTick < maxViewTick)
            {
                _viewTick    = maxViewTick;
                _accumulator = 0f;
            }
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

        /// <summary>
        /// Набран ли требуемый запас. Пока показ НЕ ИДЁТ — считается набранным: разгонять сим впереди
        /// несуществующего показа значит уехать от начала боя, а игрок обязан увидеть бой с начала.
        /// </summary>
        public bool HasFullLead => !IsPlaying || Lead >= _targetLead;

        /// <summary>
        /// Запас прочности у края окна, тиков. Просчёт останавливается НЕ вплотную к границе: между
        /// проверкой и следующим кадром рендера успевает пройти ещё несколько тиков.
        /// </summary>
        private const int WindowSafetyTicks = SimConstants.TickRate;

        /// <summary>
        /// Показ подошёл к краю окна снимков: считать дальше НЕЛЬЗЯ — следующий кадр вытеснит тот,
        /// который сейчас на экране, и картинка исчезнет.
        /// <para>Случается на паузе: показ стоит, а просчёт продолжал бы уходить вперёд. Пауза
        /// останавливает показ — значит она обязана останавливать и убегание просчёта.</para>
        /// </summary>
        public bool AtWindowLimit =>
            IsPlaying && _tape.FrontTick != BattleTape.NoTick
            && _tape.FrontTick - _viewTick >= _tape.WindowTicks - WindowSafetyTicks;

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
                if (_tape.FrontTick == BattleTape.NoTick) return;

                // С какого тика начинать показ.
                // Вне боя (запас 0) — с самого свежего кадра: там игрок действует сам и обязан видеть
                // результат сразу. В бою — с САМОГО РАННЕГО, до которого можно отстать: иначе показ
                // прилипнет к фронту, а сим к этому моменту уже успел разогнаться на сотни тиков, и
                // игрок увидит последнюю секунду боя вместо боя.
                _viewTick = _targetLead > 0
                    ? Mathf.Max(_tape.OldestTick, _tape.FrontTick - _targetLead)
                    : _tape.FrontTick;
                _accumulator = 0f;
                return;
            }

            _accumulator += deltaTime;
            while (_accumulator >= SimConstants.TickDelta)
            {
                // Обогнать сим нельзя: показ упирается во фронт ленты и ждёт там (конец боя, пауза
                // просчёта). Долю кадра при этом ФИКСИРУЕМ на конце тика, а не сбрасываем в ноль:
                // сброшенная доля означала бы «начало тика», и позиция каждый кадр прыгала бы между
                // началом и концом одного и того же шага — юниты дрожали на месте.
                if (_viewTick >= _tape.FrontTick)
                {
                    _accumulator = SimConstants.TickDelta;
                    break;
                }

                _viewTick++;
                _accumulator -= SimConstants.TickDelta;
            }

            // Вне боя (требуемое отставание 0) показ идёт вплотную за симом, а не «в реальном времени
            // от старой точки»: иначе один долгий кадр навсегда оставил бы картинку позади мира.
            int maxViewTick = _tape.FrontTick - _targetLead;
            if (_viewTick < maxViewTick)
            {
                _viewTick    = maxViewTick;
                _accumulator = 0f;
            }
        }

        /// <summary>Кадр показываемого тика. <c>false</c> — показ ещё не начался или кадр вытеснен.</summary>
        public bool TryGetFrame(out IReadOnlyList<UnitSnapshot> units)
            => TryGetFrame(out units, out _);

        /// <summary>Кадр целиком: юниты и снаряды показываемого тика.</summary>
        public bool TryGetFrame(
            out IReadOnlyList<UnitSnapshot> units, out IReadOnlyList<ProjectileSnapshot> projectiles)
        {
            if (_viewTick == BattleTape.NoTick)
            {
                units       = null;
                projectiles = null;
                return false;
            }
            return _tape.TryGetFrame(_viewTick, out units, out projectiles);
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

        /// <summary>Сбросить момент показа (dev-рестарт боя): показ начнёт с первого нового кадра.</summary>
        public void Reset()
        {
            _viewTick    = BattleTape.NoTick;
            _accumulator = 0f;
        }
    }
}
