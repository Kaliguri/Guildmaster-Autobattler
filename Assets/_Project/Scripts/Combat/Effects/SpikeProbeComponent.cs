using System;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// СПАЙК S1 (вики «12» §5). Combat-тип, реализующий Data-маркер <see cref="IEffectComponent"/>,
    /// чтобы доказать кросс-сборочный <c>[SerializeReference]</c>: экземпляр этого класса должен
    /// сериализоваться в поле <c>EffectData._components</c> (сборка Data) и пережить reimport,
    /// хотя Data НЕ ссылается на Combat вверх (вики «10» §2.2).
    /// </summary>
    /// <remarks>
    /// Временный probe — удаляется в Stage 3, когда появляется реальная иерархия
    /// <c>IRuntimeEffectComponent</c> и первые настоящие компоненты.
    /// </remarks>
    [Serializable]
    public sealed class SpikeProbeComponent : IEffectComponent
    {
        public int Marker;
    }
}
