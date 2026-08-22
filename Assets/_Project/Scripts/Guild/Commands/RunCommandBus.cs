using System;

namespace Guildmaster.Guild.Commands
{
    /// <summary>
    /// Локальная шина команд забега: нумерует, штампует временем, применяет и дописывает в лог.
    /// Единственная реализация на сегодня — соло идёт этим же путём, и это не формальность: путь,
    /// работающий мимо лога, кооп обнаружил бы первым же расхождением состояний.
    /// </summary>
    /// <remarks>
    /// <b>Штамп времени берётся здесь из системных часов.</b> Абстракции часов в проекте нет, и заводить
    /// её ради одного поля — оверинжиниринг: кому нужен свой штамп (тест, а в коопе — команда, пришедшая
    /// от другого игрока уже с его временем), подаёт готовую команду через <see cref="Submit"/>.
    /// <para><b>Счётчик номеров — свой у игрока</b> и живёт столько же, сколько забег: у нового забега
    /// номера начинаются заново вместе с очищенным логом, иначе его первые команды сошли бы за дубли.</para>
    /// </remarks>
    public sealed class RunCommandBus : ISessionRunCommands
    {
        private readonly RunCommandApplier _applier;
        private readonly RunCommandLog     _log;
        private readonly int               _playerId;

        private int _sequence;

        public RunCommandBus(RunCommandApplier applier, RunCommandLog log)
        {
            _applier  = applier;
            _log      = log;
            _playerId = RunCommand.LocalPlayerId;
        }

        /// <summary>Журнал применённых команд — для реплея, аудита и хвоста при реконнекте.</summary>
        public RunCommandLog Log => _log;

        /// <summary>
        /// Команда применена и состояние забега изменилось. Поднимается ОДИН раз на успешное применение
        /// — дубли и пустые команды до сюда не доходят.
        /// </summary>
        /// <remarks>
        /// Существует ради коопа: гость держит присланное состояние и своих изменений не считает, поэтому
        /// хосту нужен момент «теперь у меня новое — раздай». Событие живёт на шине, а не у держателя
        /// состояния, потому что шина — единственная дорога записи, и подписчик здесь не может пропустить
        /// изменение, прошедшее мимо него.
        /// </remarks>
        public event Action<RunCommand> Applied;

        /// <summary>
        /// Применить готовую команду. Вход для теста и для будущей сети: у команды, приехавшей от другого
        /// игрока, уже есть и номер, и его собственное время — переприсваивать их значило бы потерять
        /// ровно то, ради чего они существуют.
        /// </summary>
        /// <returns>
        /// <c>false</c>, если команда — дубль (пара «игрок, номер» уже применялась) либо применять было
        /// нечего. Дубль не ошибка: именно так выглядит повтор после реконнекта, и ответ на него —
        /// «уже применено», а не второе списание.
        /// </returns>
        public bool Submit(in RunCommand command)
        {
            if (_log.WasApplied(in command)) return false;
            if (!_applier.Apply(in command)) return false;

            _log.Append(in command);
            Applied?.Invoke(command);
            return true;
        }

        /// <summary>
        /// Забыть номера и лог (новый забег, загрузка другого). Зовётся вместе со сменой
        /// <see cref="RunStateService.Current"/>: лог — проекция ОДНОГО забега, и переживать его он не
        /// должен.
        /// </summary>
        public void ResetForNewRun()
        {
            _log.Clear();
            _sequence = 0;
        }

        public bool SetSlotPosition(int slotIndex, UnityEngine.Vector2 position) =>
            Submit(Next(RunCommandKind.SetSlotPosition, slotIndex: slotIndex, x: position.x, y: position.y));

        public bool SetSlotRelic(int slotIndex, string relicId) =>
            Submit(Next(RunCommandKind.SetSlotRelic, slotIndex: slotIndex, text: relicId));

        public bool SetSlotInBattle(int slotIndex, bool inBattle) =>
            Submit(Next(RunCommandKind.SetSlotInBattle, slotIndex: slotIndex, amount: inBattle ? 1 : 0));

        public bool SwapSlots(int a, int b) =>
            Submit(Next(RunCommandKind.SwapSlots, slotIndex: a, amount: b));

        public bool SetSlotItem(int slotIndex, int itemSlot, string itemId) =>
            Submit(Next(RunCommandKind.SetSlotItem, slotIndex: slotIndex, amount: itemSlot, text: itemId));

        public void AddGold(int delta) => Submit(Next(RunCommandKind.AddGold, amount: delta));

        public void RemoveRelic(string relicId) => Submit(Next(RunCommandKind.RemoveRelic, text: relicId));

        public void AwardBattleReward() => Submit(Next(RunCommandKind.AwardBattleReward));

        public void ChooseNode(string nodeId) => Submit(Next(RunCommandKind.ChooseNode, text: nodeId));

        public void InflictInjury(int slotIndex, int rollSeed) =>
            Submit(Next(RunCommandKind.InflictInjury, slotIndex: slotIndex, amount: rollSeed));

        public void HealInjury(int slotIndex, string consequenceId, bool payGold) =>
            Submit(Next(RunCommandKind.HealInjury, slotIndex: slotIndex,
                        amount: payGold ? 1 : 0, text: consequenceId));

        public bool RequestSave() => _applier.Save();

        private RunCommand Next(RunCommandKind kind, int slotIndex = -1, int amount = 0,
            string text = null, float x = 0f, float y = 0f) =>
            new RunCommand(kind, _playerId, _sequence++,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), slotIndex, amount, text, x, y);
    }
}
