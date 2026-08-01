namespace Guildmaster.Guild.Commands
{
    /// <summary>
    /// Глаголы изменения забега — то, что зовёт игра вместо прямых мутаторов <see cref="RunStateService"/>.
    /// <para><b>Инвариант, который держит компилятор:</b> мутаторы забега <c>internal</c> для сборки
    /// <c>Guildmaster.Guild</c>, поэтому из <c>Game</c> и <c>UI</c> в <see cref="RunState"/> нельзя
    /// записать иначе как через эти глаголы. Комментарий «ходите через шину» видел бы только тот, кто и
    /// так собирался; барьер видит каждый.</para>
    /// <para><b>В коопе появится вторая реализация</b>, отправляющая интент хосту; вызывающие не
    /// изменятся. Ради этого глаголы ничего не возвращают, кроме факта «принято локально»: ответ «вышло
    /// или нет» в сети приходит позже, и метод, возвращающий успех сразу, пришлось бы переписывать
    /// вместе со всеми вызывающими.</para>
    /// </summary>
    public interface IRunCommands
    {
        /// <summary>Запомнить позицию сосуда на арене (перетаскивание в расстановке).</summary>
        bool SetSlotPosition(int slotIndex, UnityEngine.Vector2 position);

        /// <summary>Поставить кит на сосуд напрямую, минуя запас (drag реликвии на юнита в расстановке).</summary>
        bool SetSlotRelic(int slotIndex, string relicId);

        /// <summary>Изменить золото забега (±).</summary>
        void AddGold(int delta);

        /// <summary>Убрать один экземпляр реликвии из запаса.</summary>
        void RemoveRelic(string relicId);

        /// <summary>Начислить награду золотом за победу в бою (величина — из конфига).</summary>
        void AwardBattleReward();
    }
}
