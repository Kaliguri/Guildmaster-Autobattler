using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Смещение» Арканиста (карточка [[the-rift]]): pre-damage реактив с периодом. По первому удару
    /// после готовности периода носитель УКЛОНЯЕТСЯ — уходит телепортом на <see cref="_shiftDistance"/>
    /// в сторону своего отступления и получает <see cref="_shieldBuff"/>.
    /// <para><b>Это уклонение, а не блок и не отмена</b> (уточнение Макса 2026-07-30): атака противника
    /// состоялась — он потратил удар, отыграл замах и довёл его до конца, — но пришла в пустое место,
    /// потому что цель успела выпасть из точки. Урона поэтому нет, и реактивы «на удар» не будят: бить
    /// было некого. Для показа это промах, а не сработавшая защита.</para>
    /// <para><b>Уклонение детерминированное, а не шансовое:</b> сработает именно первый подходящий удар
    /// после готовности периода. Никакого «шанса уклониться» — правило нулевого выходного рандома.</para>
    /// <para><b>Числа:</b> <c>_periodSeconds</c> — как часто кит выпадает из-под удара (Арканист = 15);
    /// <c>_shiftDistance</c> — на сколько единиц уносит (4); <c>_shiftSpeedPerSecond</c> — скорость
    /// смещения, вместе с дистанцией задаёт его длительность (высокая: это телепорт, а не полёт);
    /// <c>_shieldBuff</c> — щит на выходе, его величина и срок живут в нём самом.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, до расчёта урона — отменённый удар не наносит ничего
    /// и не будит реактивы «на удар».</para>
    /// </summary>
    /// <remarks>
    /// Отдельный компонент, а не настройка <see cref="DodgeComponent"/>, по трём причинам: тот тратит
    /// ЗАРЯДЫ и гейтится триггером блока F (уклонение убийцы — ресурс), уходит по направлению своего
    /// намерения (перекат по ходу движения) и отменяет только авто-атаки. Смещение живёт периодом, уходит
    /// строго в тыл (маг убегает от строя, а не «куда шёл») и по режиму <see cref="_alsoRanged"/> может
    /// снимать любой прямой удар. Слить их в один означало бы шесть полей-переключателей на разные киты.
    /// <para><b>Требует дееспособности:</b> смещение — это действие, оглушённый маг из точки не выпадает.</para>
    /// <para><b>Долг:</b> блок G AI-настроек карточки («только ближний» / «ближний и дальний») сейчас
    /// живёт полем ассета, а не выбором игрока — модель AI-настроек пока не умеет переключать поведение
    /// компонента эффекта. Развилка описана в docs/roster-expansion-progress.md.</para>
    /// </remarks>
    [Serializable]
    public sealed class PhaseShiftComponent : IPreDamageComponent, IStackableComponent, IRequiresAgencyComponent
    {
        [Tooltip("Период, сек: как часто смещение готово сработать. Арканист = 15.")]
        [SerializeField] private float _periodSeconds = 15f;

        [Tooltip("Дистанция телепорта, мировых единиц. Арканист = 4. 0 = смещение на месте (только щит).")]
        [SerializeField] private float _shiftDistance = 4f;

        [Tooltip("Скорость смещения, ед/сек. Высокая намеренно: это выпадение из точки, а не полёт. 0 = общий дефолт смещения.")]
        [SerializeField] private float _shiftSpeedPerSecond = 40f;

        [Tooltip("Щит на выходе из смещения (величина и длительность — в нём самом).")]
        [SerializeField] private EffectData _shieldBuff;

        [Tooltip("Срабатывать и на ДАЛЬНИЕ удары: тогда носитель смещается на месте — снаряд пролетает мимо, " +
                 "щит выдаётся так же. Выкл = только ближний бой (дефолт карточки).")]
        [SerializeField] private bool _alsoRanged;

        public void OnApply(in EffectContext ctx)
        {
            // Один «заряд» = одно смещение за период: заряды RuntimeEffect уже несут таймер перезарядки,
            // отдельный счётчик тиков в компоненте был бы вторым владельцем того же факта (компоненты
            // stateless — состояние живёт в эффекте).
            ctx.Effect.ArmCharges(1);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ перевзводит период: иначе повторное наложение пассивки дарило бы бесплатное
            // смещение вне очереди (та же ловушка, что у зарядов «Отхода», 07 §3.8 B2).
        }

        /// <summary>Смещение выводит цель из точки удара: то же семейство, что и отход.</summary>

        public int Priority => ReactionPriority.Evade;


        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return; // удар уже отменён другим компонентом

            // Доты и ответки реактивов смещение не будят: из-под яда телепортом не выйдешь, а иначе
            // период сгорал бы на первом же тике горения, ни от чего не спасая.
            if (!incoming.IsDirectHit) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;
            if (!SourceMatchesMode(in incoming)) return;

            int periodTicks = Mathf.Max(1, Mathf.RoundToInt(_periodSeconds * SimConstants.TickRate));
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, periodTicks)) return;

            // Сначала уходим, потом гасим урон — порядок соответствует смыслу: удар прилетает в точку,
            // из которой цель уже ушла. Обратный порядок дал бы «сначала не получил, потом отошёл», то
            // есть блок с отходом, а это другая механика.
            Shift(self, in ctx);
            result.Negated = true;
        }

        /// <summary>
        /// Выпадение из точки: щит плюс уход в тыл. Смещение идёт общим швом (носитель = и цель, и
        /// источник), поэтому об стену не наказывается и урона не приносит — в отличие от толчка.
        /// </summary>
        private void Shift(RuntimeUnit self, in EffectContext ctx)
        {
            if (_shieldBuff != null) ctx.Combat.ApplyEffect(self, _shieldBuff, self);
            if (_shiftDistance <= 0f) return;

            // «Куда отступал бы при побеге» — сторона своего края арены: тот же ориентир, которым
            // FleeSteering уводит юнита домой, поэтому маг всегда уходит ЗА строй, а не в чужой.
            ctx.Combat.Displace(new DisplaceRequest(
                self, self, FleeSteering.HomeDir(self), _shiftDistance,
                cannonball: false, damage: 0f, damageType: self.AutoAttackDamageType, width: 0f,
                speedPerSecond: _shiftSpeedPerSecond));
        }

        /// <summary>
        /// Подходит ли источник удара режиму: ближний бой всегда, дальний — только при
        /// <see cref="_alsoRanged"/>. Источника нет (ловушка, окружение) — считаем, что не подходит:
        /// период дорог, и тратить его на удар без автора нельзя.
        /// </summary>
        private bool SourceMatchesMode(in DamageRequest incoming)
        {
            RuntimeUnit source = incoming.Source;
            if (source?.Unit == null) return false;
            return _alsoRanged || source.Unit.AttackType == AttackType.Melee;
        }
    }
}
