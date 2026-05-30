using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Рантайм-представление юнита на один бой. POCO — без MonoBehaviour и ScriptableObject.
    /// Создаётся через <see cref="RuntimeUnitFactory"/>; владеет <see cref="Stats"/>,
    /// текущим HP/ресурсом/щитом и состоянием движения (вики «10» §5.2).
    /// </summary>
    public sealed class RuntimeUnit
    {
        /// <summary>Уникальный идентификатор юнита в рамках текущего боя.</summary>
        public int Id;

        /// <summary>Команда: 0 = союзники (левая сторона), 1 = враги (правая).</summary>
        public int Team;

        /// <summary>Собранные статы с поддержкой слоистых модификаторов.</summary>
        public Stats Stats;

        /// <summary>Текущее здоровье. DeathSystem помечает мёртвым при ≤ 0.</summary>
        public float CurrentHP;

        /// <summary>Текущий ресурс (мана/ярость). Фаза 2.</summary>
        public float CurrentResource;

        /// <summary>Текущий щит: поглощает урон до вычета из HP.</summary>
        public float CurrentShield;

        /// <summary>Позиция центра юнита в мировых координатах (непрерывное поле, без Rigidbody).</summary>
        public Vector2 Position;

        /// <summary>Позиция на предыдущем тике — для интерполяции вида (60 fps рендер, 30 Hz сим).</summary>
        public Vector2 PreviousPosition;

        /// <summary>Текущая цель для автоатаки и движения. Null = цель не назначена.</summary>
        public RuntimeUnit CurrentTarget;

        /// <summary>Кулдаун автоатаки в секундах. 0 = готов к атаке.</summary>
        public float AttackCooldown;

        /// <summary>Помечен DeathSystem — исключается из всех систем с текущего тика.</summary>
        public bool IsDead;

        /// <summary>SO «Чемпион»: тип атаки, стат-блок, эффекты (Фаза 2).</summary>
        public RelicData Relic;

        /// <summary>SO «Пилот»: идентичность, таланты (Фаза 2/4).</summary>
        public VesselData Vessel;
    }
}
