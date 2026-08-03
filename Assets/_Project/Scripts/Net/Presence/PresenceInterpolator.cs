using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Net.Presence
{
    /// <summary>
    /// Чужие курсоры на экране: держит короткий буфер приходящих состояний и выдаёт положение на
    /// заданный момент — гладко между пакетами и по инерции, когда пакет не пришёл.
    /// <para><b>Плавность делается ЗДЕСЬ, а не частотой отправки.</b> Это главное, что стоит понять про
    /// присутствие: сколько бы пакетов ни слали, картинка гладкая ровно настолько, насколько хорошо
    /// приёмник умеет между ними интерполировать.</para>
    /// <para><b>Эрмит, а не линейная.</b> Линейная интерполяция между точками даёт заметное дрожание на
    /// изломах траектории — а курсор в коопе только из изломов и состоит. Скорость приезжает в пакете
    /// именно ради этого: по паре «точка + скорость» строится кривая, у которой на стыке совпадает не
    /// только положение, но и направление.</para>
    /// </summary>
    /// <remarks>
    /// <b>Буфер короткий — один-два интервала</b> (решение Макса 31.07.2026): присутствие эфемерно, и
    /// платить за него задержкой в три интервала, как за игровое состояние, незачем. Потерянный пакет
    /// экстраполируется по последней скорости, и этого никто не замечает — при условии, что экстраполяция
    /// ограничена по времени (см. <see cref="MaxExtrapolationSeconds"/>): курсор, улетевший по инерции в
    /// бесконечность, читается как баг, а замерший — как «человек отошёл».
    /// </remarks>
    public sealed class PresenceInterpolator
    {
        /// <summary>Буфер задержки: чуть больше интервала отправки при 128 Гц.</summary>
        public const float BufferSeconds = 0.012f;

        /// <summary>
        /// Сколько экстраполируем по инерции, потеряв пакеты. Дальше курсор просто стоит: за 100 мс
        /// движение руки успевает измениться, и продолжать его — врать увереннее, чем мы знаем.
        /// </summary>
        public const float MaxExtrapolationSeconds = 0.1f;

        private readonly struct Sample
        {
            public readonly PresenceState State;
            public readonly float         At;

            public Sample(in PresenceState state, float at)
            {
                State = state;
                At    = at;
            }
        }

        private readonly Dictionary<int, Sample> _last     = new Dictionary<int, Sample>(8);
        private readonly Dictionary<int, Sample> _previous = new Dictionary<int, Sample>(8);

        /// <summary>Сколько чужих курсоров сейчас известно.</summary>
        public int Count => _last.Count;

        /// <summary>Игроки, чьи курсоры известны.</summary>
        public IEnumerable<int> Players => _last.Keys;

        /// <summary>
        /// Принять состояние. Пакет с номером не новее уже принятого отбрасывается: канал ненадёжный и
        /// не сохраняет порядок, а откат курсора назад читается как рывок.
        /// </summary>
        public bool Push(in PresenceState state, float receivedAt)
        {
            if (_last.TryGetValue(state.PlayerId, out Sample known))
            {
                // Сравнение с переполнением: ushort по кругу, и на 65536-м пакете «меньше» стало бы
                // «больше», а курсор — замереть навсегда.
                short delta = unchecked((short)(state.Sequence - known.State.Sequence));
                if (delta <= 0) return false;

                _previous[state.PlayerId] = known;
            }

            _last[state.PlayerId] = new Sample(in state, receivedAt);
            return true;
        }

        /// <summary>Убрать курсор ушедшего игрока.</summary>
        public void Remove(int playerId)
        {
            _last.Remove(playerId);
            _previous.Remove(playerId);
        }

        public void Clear()
        {
            _last.Clear();
            _previous.Clear();
        }

        /// <summary>
        /// Где рисовать курсор игрока в момент <paramref name="now"/>. <c>false</c> — про этого игрока
        /// ничего не известно.
        /// </summary>
        public bool TrySample(int playerId, float now, out PresenceState state, out Vector2 position)
        {
            state    = default;
            position = default;

            if (!_last.TryGetValue(playerId, out Sample last)) return false;

            state = last.State;

            // Смотрим не «сейчас», а «сейчас минус буфер»: пакет, который вот-вот придёт, для нас ещё
            // будущее, и без этого сдвига интерполировать было бы не к чему.
            float target = now - BufferSeconds;

            if (_previous.TryGetValue(playerId, out Sample prev) && target <= last.At && last.At > prev.At)
            {
                float span = last.At - prev.At;
                float t    = Mathf.Clamp01((target - prev.At) / span);
                position   = Hermite(prev.State.Cursor, prev.State.Velocity,
                                     last.State.Cursor, last.State.Velocity, t, span);
                return true;
            }

            // Свежих данных нет — идём по инерции, но недолго.
            float ahead = Mathf.Clamp(target - last.At, 0f, MaxExtrapolationSeconds);
            position = last.State.Cursor + last.State.Velocity * ahead;
            return true;
        }

        /// <summary>
        /// Кривая Эрмита между двумя состояниями. Касательные — реальные скорости, умноженные на
        /// длительность отрезка: без множителя кривая «не знает», за какое время проходится участок, и
        /// выгибается тем сильнее, чем реже пакеты.
        /// </summary>
        private static Vector2 Hermite(Vector2 p0, Vector2 v0, Vector2 p1, Vector2 v1, float t, float span)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            float h00 =  2f * t3 - 3f * t2 + 1f;
            float h10 =       t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 =       t3 -      t2;

            return h00 * p0 + h10 * span * v0 + h01 * p1 + h11 * span * v1;
        }
    }
}
