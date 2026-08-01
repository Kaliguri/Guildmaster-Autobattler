using UnityEngine;

namespace Guildmaster.Net.Presence
{
    /// <summary>
    /// Решает, пора ли отправлять своё присутствие: потолок частоты плюс dirty-check.
    /// <para><b>128 Гц — решение Макса 31.07.2026, и цифра неслучайна.</b> Частота отправки НЕ покупает
    /// плавность: плавность даёт интерполяция на приёме. Она покупает размер буфера, а через него —
    /// задержку. Практическое правило Фидлера — буфер около трёх интервалов отправки; у нас взят
    /// короткий (один-два), потому что присутствие эфемерно и потерянный пакет экстраполируется. При 128
    /// Гц это ~8-16 мс против ~85 мс на шестидесяти — разница, которая на быстрых кооп-жестах (перехват
    /// из рук, кубик) покупается почти даром.</para>
    /// <para><b>Почему не 240:</b> RTT по Steam relay стоит 30-80 мс независимо от числа пакетов, и
    /// выигрыш тонет в том, что мы не контролируем; рука на мыши — низкочастотный сигнал, выше 128 Гц
    /// передаётся шум сенсора; а каждый пакет стоит не только байтов, но и сериализации с очередями.</para>
    /// </summary>
    /// <remarks>
    /// <b>Курсор стоит — пакетов ноль.</b> Это не оптимизация, а причина, по которой высокую частоту
    /// вообще можно себе позволить: платим только за движение.
    /// </remarks>
    public sealed class PresenceSender
    {
        /// <summary>Потолок частоты отправки, Гц (решение Макса 31.07.2026).</summary>
        public const float MaxRateHz = 128f;

        /// <summary>
        /// Насколько курсор должен сдвинуться, чтобы это считалось движением. Половина шага упаковки:
        /// меньшее всё равно не переживёт квантование, и гнать его значит платить за ничто.
        /// </summary>
        public const float MoveEpsilon = 0.5f / Tape.TapeQuantization.PositionScale;

        private readonly float _minInterval = 1f / MaxRateHz;

        private PresenceState _last;
        private Vector2       _lastCursor;
        private float         _lastSentAt = float.NegativeInfinity;
        private float         _lastSampleAt;
        private bool          _hasSample;
        private ushort        _sequence;

        /// <summary>Сколько пакетов отправлено — для диагностики и тестов, не для логики.</summary>
        public int SentCount { get; private set; }

        /// <summary>Последнее, что было отправлено.</summary>
        public PresenceState Last => _last;

        /// <summary>
        /// Предложить текущее состояние курсора. Возвращает <c>true</c>, если пора отправлять — тогда
        /// <paramref name="state"/> заполнен готовым к упаковке снимком.
        /// </summary>
        /// <param name="now">Текущее время, секунды. Подаётся снаружи, чтобы поведение проверялось тестом.</param>
        public bool TrySample(Vector2 cursor, int playerId, float now, out PresenceState state,
            int hoveredId = PresenceState.Nothing, int heldId = PresenceState.Nothing)
        {
            state = default;

            bool changed = !_hasSample
                           || (cursor - _lastCursor).sqrMagnitude > MoveEpsilon * MoveEpsilon
                           || hoveredId != _last.HoveredId
                           || heldId    != _last.HeldId;

            // Скорость считается по фактическому промежутку между ЗАМЕРАМИ, а не по частоте отправки:
            // приёмник экстраполирует именно ею, и завышенная скорость увела бы чужой курсор в сторону
            // на первой же потере пакета.
            Vector2 velocity = Vector2.zero;
            if (_hasSample && now > _lastSampleAt)
                velocity = (cursor - _lastCursor) / (now - _lastSampleAt);

            _lastCursor   = cursor;
            _lastSampleAt = now;
            _hasSample    = true;

            if (!changed) return false;                       // курсор стоит — молчим
            if (now - _lastSentAt < _minInterval) return false; // потолок частоты

            state = new PresenceState(playerId, _sequence++, cursor, velocity, hoveredId, heldId);

            _last       = state;
            _lastSentAt = now;
            SentCount++;
            return true;
        }

        /// <summary>Забыть историю (новая сессия): номера и позиция начинаются заново.</summary>
        public void Reset()
        {
            _hasSample  = false;
            _lastSentAt = float.NegativeInfinity;
            _sequence   = 0;
            SentCount   = 0;
            _last       = default;
        }
    }
}
