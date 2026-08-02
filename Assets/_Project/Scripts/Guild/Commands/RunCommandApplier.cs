namespace Guildmaster.Guild.Commands
{
    /// <summary>
    /// Единственное место, где команда превращается в изменение <see cref="RunState"/>. Отдельно от шины
    /// намеренно: так лог можно прогнать по чистому состоянию без сети, часов и счётчиков — на этом стоит
    /// проверка «один лог → один <see cref="RunState"/>», а с ней и реплей, и реконнект.
    /// </summary>
    /// <remarks>
    /// Обработчик знает про виды команд и ничего не знает про то, откуда они пришли. Новый вид команды —
    /// новая ветка здесь и новый глагол в <see cref="IRunCommands"/>; ни шина, ни лог при этом не
    /// меняются.
    /// </remarks>
    public sealed class RunCommandApplier
    {
        private readonly RunStateService _state;

        public RunCommandApplier(RunStateService state) => _state = state;

        /// <summary>
        /// Зафиксировать состояние на диск. Не команда и в лог не попадает: сохранение ничего не меняет,
        /// оно закрепляет уже изменённое — но трогает состояние, а значит идёт отсюда, как и всё
        /// остальное. <c>false</c> — забега нет, фиксировать нечего.
        /// </summary>
        public bool Save()
        {
            if (_state.Current == null) return false;

            _state.Autosave();
            return true;
        }

        /// <summary>
        /// Применить команду. Возвращает <c>false</c>, если применить было нечего (нет забега, слот вне
        /// ростера, пустой id) — тогда команда в лог не попадает: журнал хранит то, что случилось.
        /// </summary>
        public bool Apply(in RunCommand command)
        {
            switch (command.Kind)
            {
                case RunCommandKind.SetSlotPosition:
                    return _state.SetSlotPosition(
                        command.SlotIndex, new UnityEngine.Vector2(command.X, command.Y));

                case RunCommandKind.SetSlotRelic:
                    return _state.SetSlotRelic(command.SlotIndex, command.Text);

                case RunCommandKind.AddGold:
                    if (_state.Current == null || command.Amount == 0) return false;
                    _state.AddGold(command.Amount);
                    return true;

                case RunCommandKind.RemoveRelic:
                    if (_state.Current == null || string.IsNullOrEmpty(command.Text)) return false;
                    _state.RemoveRelic(command.Text);
                    return true;

                case RunCommandKind.AwardBattleReward:
                    if (_state.Current == null) return false;
                    _state.AwardBattleReward();
                    return true;

                // Вид команды без ветки — это забытая проводка, а не «ничего страшного»: изменение молча
                // не произошло бы у всех сразу, и разъезд состояний нашёлся бы много позже (политика
                // фолбэков: наше авторство = громкий отказ).
                default:
                    UnityEngine.Debug.LogError(
                        $"[RunCommandApplier] - вид команды {command.Kind} не обработан: {command}");
                    return false;
            }
        }
    }
}
