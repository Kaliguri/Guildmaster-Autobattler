using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Гостевая половина раздачи: раз в кадр просит хоста повторить чанки, которые не доехали.
    /// </summary>
    /// <remarks>
    /// <b>Почему отдельный тик, а не таймер внутри приёмника.</b> <see cref="TapeIntake"/> принимает
    /// время параметром намеренно: свой таймер сделал бы повторные запросы недетерминированными, а
    /// весь смысл chaos-слоя в том, чтобы потеря воспроизводилась по сиду. Значит время обязан подать
    /// кто-то снаружи, и в игре это здесь.
    /// <para><b>Время НЕмасштабированное:</b> пауза останавливает показ, но не сеть. На паузе дыра в
    /// ленте должна закрываться так же, как в бою, иначе снятие паузы упрётся в недоехавший чанк.</para>
    /// <para><b>Собирается только у гостя</b> (см. <c>CombatLifetimeScope</c>): у владельца дыр в
    /// собственной ленте не бывает и просить нечего.</para>
    /// </remarks>
    public sealed class TapeIntakePump : ITickable
    {
        private readonly TapeIntake _intake;

        public TapeIntakePump(TapeIntake intake) => _intake = intake;

        public void Tick() => _intake.RequestMissing(Time.unscaledTime);
    }
}
