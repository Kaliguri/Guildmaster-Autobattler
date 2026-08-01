using System;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Отдаёт события боя ТОГДА, КОГДА их показали. Сим уже посчитал их на окно опережения раньше;
    /// презентация подписывается сюда вместо симуляции, и тогда цифра урона, звук и вспышка совпадают
    /// с кадром, а не опережают его на десять секунд.
    /// <para><b>Почему id, а не <c>RuntimeUnit</c>:</b> событие приезжает из прошлого, живой юнит уже в
    /// будущем. Кому нужно состояние — берёт снимок того же тика из ленты.</para>
    /// <para><b>Конец боя тоже едет здесь</b>, и это не мелочь: исход, награды и экран итогов обязаны
    /// ждать показ, иначе победа объявляется, пока на арене ещё дерутся.</para>
    /// </summary>
    public sealed class BattleTapeDispatcher
    {
        private readonly BattleTape _tape;

        private int _cursor;      // индекс первого ещё не отданного события
        private int _shownTick = BattleTape.NoTick;

        public BattleTapeDispatcher(BattleTape tape)
        {
            _tape = tape;
        }

        /// <summary>Юнит вышел на арену (показан).</summary>
        public event Action<int> UnitSpawned;

        /// <summary>Юнит умер на экране.</summary>
        public event Action<int> UnitDied;

        /// <summary>Удар показан: источник, цель, результат.</summary>
        public event Action<int, int, DamageResult> DamageDealt;

        /// <summary>Лечение показано: источник, цель, величина.</summary>
        public event Action<int, int, float> Healed;

        /// <summary>Удар по цели отменён целиком — показ рисует «evade».</summary>
        public event Action<int> AttackEvaded;

        /// <summary>Начался замах: источник, цель.</summary>
        public event Action<int, int> AttackStarted;

        /// <summary>Замах прерван.</summary>
        public event Action<int> AttackInterrupted;

        /// <summary>Зона удара (линия/круг) — геометрия уже готовая.</summary>
        public event Action<AreaHit> AreaHit;

        /// <summary>Бой кончился НА ЭКРАНЕ. Именно этого ждёт флоу боя, а не конца просчёта.</summary>
        public event Action<BattleOutcome> BattleEnded;

        /// <summary>Dev-рестарт: показ прошлого боя оборван.</summary>
        public event Action BattleReset;

        /// <summary>Каст показан: id кастера и определение способности (чем исполнен приём).</summary>
        public event Action<int, Data.Definitions.AbilityData> AbilityCast;

        /// <summary>
        /// Началась подготовка (показано): id кастера, её длительность в секундах и определение
        /// способности — показ по нему выбирает, что зажечь на подводке.
        /// </summary>
        public event Action<int, float, Data.Definitions.AbilityData> AbilityCastStarted;

        /// <summary>Каст оборван (показано): id кастера. Подводка гаснет здесь.</summary>
        public event Action<int> AbilityCastInterrupted;

        /// <summary>Эффект лёг на носителя (показано): id носителя, определение эффекта.</summary>
        public event Action<int, Data.Definitions.EffectData> EffectApplied;

        /// <summary>Эффект спал с носителя (показано).</summary>
        public event Action<int, Data.Definitions.EffectData> EffectEnded;

        /// <summary>Последний тик, события которого уже отданы.</summary>
        public int ShownTick => _shownTick;

        /// <summary>Сколько событий уже отдано показу (курсор). Для диагностики.</summary>
        public int DeliveredCount => _cursor;

        /// <summary>
        /// Отдать всё, что случилось по момент показа (<paramref name="viewTick"/> плюс доля
        /// <paramref name="alpha"/>) включительно. Зовётся раз за кадр после продвижения показа; курсор
        /// гарантирует, что событие не отдаётся дважды.
        /// </summary>
        /// <param name="alpha">
        /// Доля показываемого тика, уже отыгранная на экране (<c>BattleTapePlayback.Alpha</c>). Событие
        /// внутри этого тика ждёт, пока показ дойдёт до его <see cref="TapeEvent.SubTick"/> — так вспышка,
        /// цифра, звук и hitstop встают в кадр контакта, а не на границу тика (разброс был до 33 мс).
        /// <para><b>Дефолт 1 = «тик показан целиком»</b>, то есть прежнее поведение: тому, кто долей не
        /// пользуется (тесты, диагностика, догоняющая прокрутка), ничего знать о ней не нужно.</para>
        /// </param>
        public void PumpTo(int viewTick, float alpha = 1f)
        {
            if (viewTick == BattleTape.NoTick) return;

            while (_cursor < _tape.EventCount)
            {
                TapeEvent ev = _tape.GetEvent(_cursor);
                if (ev.Tick > viewTick) break;

                // Событие показываемого тика ждёт своей доли. Прошлые тики отдаются всегда: их доля уже
                // отыграна, а «догнать» надо целиком — иначе долгий кадр потерял бы события насовсем.
                if (ev.Tick == viewTick && ev.SubTick > alpha) break;

                _cursor++;
                Raise(in ev);
            }

            _shownTick = viewTick;
        }

        /// <summary>
        /// Сбросить курсор (dev-рестарт боя): лента уже очищена, значит и отданное надо забыть, иначе
        /// первые события нового боя окажутся «уже показанными».
        /// </summary>
        public void Reset()
        {
            _cursor    = 0;
            _shownTick = BattleTape.NoTick;
        }

        private void Raise(in TapeEvent ev)
        {
            switch (ev.Kind)
            {
                case TapeEventKind.UnitSpawned:
                    UnitSpawned?.Invoke(ev.SourceId);
                    break;
                case TapeEventKind.UnitDied:
                    UnitDied?.Invoke(ev.SourceId);
                    break;
                case TapeEventKind.DamageDealt:
                    DamageDealt?.Invoke(ev.SourceId, ev.TargetId, _tape.GetDamage(ev.PayloadIndex));
                    break;
                case TapeEventKind.Healed:
                    Healed?.Invoke(ev.SourceId, ev.TargetId, ev.Amount);
                    break;
                case TapeEventKind.AttackEvaded:
                    AttackEvaded?.Invoke(ev.TargetId);
                    break;
                case TapeEventKind.AttackStarted:
                    AttackStarted?.Invoke(ev.SourceId, ev.TargetId);
                    break;
                case TapeEventKind.AttackInterrupted:
                    AttackInterrupted?.Invoke(ev.SourceId);
                    break;
                case TapeEventKind.AreaHit:
                    AreaHit?.Invoke(_tape.GetAreaHit(ev.PayloadIndex));
                    break;
                case TapeEventKind.BattleEnded:
                    BattleEnded?.Invoke(_tape.GetOutcome(ev.PayloadIndex));
                    break;
                case TapeEventKind.BattleReset:
                    BattleReset?.Invoke();
                    break;
                case TapeEventKind.AbilityCast:
                    AbilityCast?.Invoke(ev.SourceId, _tape.GetAbility(ev.PayloadIndex));
                    break;

                case TapeEventKind.AbilityCastStarted:
                    AbilityCastStarted?.Invoke(ev.SourceId, ev.Amount, _tape.GetAbility(ev.PayloadIndex));
                    break;

                case TapeEventKind.AbilityCastInterrupted:
                    AbilityCastInterrupted?.Invoke(ev.SourceId);
                    break;
                case TapeEventKind.EffectApplied:
                    EffectApplied?.Invoke(ev.TargetId, _tape.GetEffect(ev.PayloadIndex));
                    break;
                case TapeEventKind.EffectEnded:
                    EffectEnded?.Invoke(ev.TargetId, _tape.GetEffect(ev.PayloadIndex));
                    break;
            }
        }
    }
}
