using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Паспорт юнита на арене: то, что за время его присутствия не меняется — определение, команда, id.
    /// </summary>
    /// <remarks>
    /// Живёт отдельно от снимка (<see cref="UnitSnapshot"/>) по двум причинам сразу. Показ отстаёт от
    /// симуляции на окно опережения, поэтому к моменту, когда до юнита доходит картинка, живого
    /// <see cref="RuntimeUnit"/> под рукой может уже не быть — а спросить «кто это, какой у него арт»
    /// надо. И везти неизменное тридцать раз в секунду по сети значило бы платить за то, что и так
    /// известно.
    /// </remarks>
    public readonly struct UnitIdentity
    {
        public readonly UnitData Definition;
        public readonly int      Team;
        public readonly int      Id;

        public UnitIdentity(UnitData definition, int team, int id)
        {
            Definition = definition;
            Team       = team;
            Id         = id;
        }

        /// <summary>Строковый id контента — ключ звука и локализации. Пусто у болванчиков без данных.</summary>
        public string ContentId => Definition != null ? Definition.Id : null;
    }

    /// <summary>
    /// Кто есть кто на арене: id → паспорт. Второй шов показа рядом с источником кадра
    /// (<see cref="IStageFrameSource"/>) — кадр отвечает «что с ними сейчас», директория «кто они».
    /// </summary>
    /// <remarks>
    /// <b>Почему шов, а не словарь у потребителя.</b> Пока паспорта копил у себя каждый, кто рисует,
    /// наполнялись они событием спавна симуляции — и всякий показ без своей симуляции (гость в коопе,
    /// тела мира вне боя) оставался без паспортов, то есть без видов вовсе. Владелец факта «кто на
    /// арене» должен быть один и тот же для боя и для мира, а меняется вместе с источником кадра.
    /// </remarks>
    public interface IUnitDirectory
    {
        bool TryGet(int unitId, out UnitIdentity identity);

        /// <summary>Сколько паспортов известно. Для dev-диагностики: «кадры пришли, а состав — нет».</summary>
        int Count { get; }
    }
}
