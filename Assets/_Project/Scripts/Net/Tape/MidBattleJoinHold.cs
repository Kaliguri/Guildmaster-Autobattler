using System;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Подключение посреди боя: бой встаёт на общую паузу, пока напарник догружает ленту, и снимается с
    /// неё через короткий отсчёт — чтобы оба увидели одно и то же место с одного момента.
    /// </summary>
    /// <remarks>
    /// <b>Схема — решение Макса 04.08.2026:</b> «Когда кто-то пытается подключиться - все встает на
    /// паузу. „Загрузка“... пишется обоим. Ему передается вся информация, он запускает View в том месте.
    /// где были все игроки до этого. Потом отчет и продолжения.»
    /// <para><b>Сигналом служит просьба «дай бой с начала»,</b> а не подключение пира. Отдельного
    /// события мы не заводили: рукопожатие говорит «в сессии новый человек», но ждать его надо ровно
    /// тогда, когда он просит ленту, — и просит он сам, когда его боевой скоуп готов её принять.</para>
    /// <para><b>Ответа гостя «я готов» не ждём.</b> Лента едет надёжным каналом, и очередь, опустевшая
    /// у хоста, означает «всё отправлено и дойдёт». Ответ добавил бы третье состояние и второй способ
    /// узнать одно и то же — а вместе с ним и случай «ответ потерялся, бой стоит навсегда».</para>
    /// <para><b>Пауза общая и потому видна обоим</b> (<see cref="BattleControlRelay"/>): её же читает
    /// интерфейс, показывая, что игра ждёт. Симуляцию останавливать не нужно — она сама упрётся в окно
    /// снимков, пока показ стоит.</para>
    /// </remarks>
    public sealed class MidBattleJoinHold : ITickable, IDisposable
    {
        /// <summary>Сколько ждать перед возобновлением, когда лента ушла. Даёт вернуться в кадр.</summary>
        public const float CountdownSeconds = 3f;

        /// <summary>
        /// Предел ожидания догрузки. Гость может отвалиться ровно в момент подключения — самый частый
        /// способ уронить хост в P2P, — и бой не имеет права стоять из-за человека, которого уже нет.
        /// </summary>
        public const float TimeoutSeconds = 30f;

        private readonly TapeStreamer       _streamer;
        private readonly BattleControlRelay _relay;

        private bool  _holding;
        private float _waited;
        private float _countdown;

        public MidBattleJoinHold(TapeStreamer streamer, BattleControlRelay relay)
        {
            _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
            _relay    = relay;

            _streamer.WholeBattleRequested += OnWholeBattleRequested;
        }

        public void Dispose() => _streamer.WholeBattleRequested -= OnWholeBattleRequested;

        /// <summary>Ждём ли мы сейчас догрузку напарника — по этому признаку интерфейс пишет «Загрузка».</summary>
        public bool Holding => _holding;

        /// <summary>Сколько осталось до возобновления; ноль — отсчёт ещё не начат.</summary>
        public float CountdownLeft => _countdown;

        private void OnWholeBattleRequested(int peer, int chunks)
        {
            // Бой только начался — истории нет, догружать нечего, и держать некого. Иначе пауза
            // срабатывала бы на каждом штатном входе в бой вдвоём.
            if (chunks <= 0) return;

            _holding   = true;
            _waited    = 0f;
            _countdown = 0f;
            _relay?.RequestPause(true);

            Debug.Log($"[MidBattleJoinHold] пир {peer} вошёл посреди боя: держим паузу, {chunks} чанков");
        }

        /// <summary>Время нескалированное: пауза останавливает показ, а не ожидание.</summary>
        public void Tick() => Advance(Time.unscaledDeltaTime);

        /// <summary>
        /// Прожить <paramref name="dt"/> секунд ожидания. Время приходит параметром по той же причине,
        /// что и в <see cref="TapeIntake"/>: со своим таймером внутри отсчёт и таймаут проверялись бы
        /// только настоящим ожиданием в тридцать секунд.
        /// </summary>
        public void Advance(float dt)
        {
            if (!_holding) return;

            if (_countdown > 0f)
            {
                _countdown -= dt;
                if (_countdown > 0f) return;

                _holding = false;
                _relay?.RequestPause(false);
                return;
            }

            _waited += dt;

            if (_streamer.BackfillRemaining == 0)
            {
                _countdown = CountdownSeconds;
                return;
            }

            if (_waited < TimeoutSeconds) return;

            // Не дождались. Бой продолжается без него: гость, если он ещё здесь, доберёт ленту обычными
            // запросами потерянного — просто увидит бой не с той же секунды, что мы.
            Debug.LogWarning($"[MidBattleJoinHold] догрузка не кончилась за {TimeoutSeconds} с " +
                             $"(осталось {_streamer.BackfillRemaining}) — продолжаем без ожидания");
            _holding   = false;
            _countdown = 0f;
            _relay?.RequestPause(false);
        }
    }
}
