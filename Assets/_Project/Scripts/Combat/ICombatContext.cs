using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Шов «логика эффектов/способностей ↔ боевой мир». Единственная точка входа для мутаций
    /// симуляции из систем и компонентов эффектов. Реализует <see cref="CombatSimulation"/>
    /// (вики «10» §5.5).
    /// </summary>
    public interface ICombatContext
    {
        /// <summary>Применить урон через полный пайплайн.</summary>
        void DealDamage(in DamageRequest req);

        /// <summary>Исцелить юнита. Не выходит за MaxHP.</summary>
        void Heal(RuntimeUnit target, float amount, RuntimeUnit source);

        /// <summary>Создать снаряд в симуляции.</summary>
        void SpawnProjectile(in ProjectileSpawn spawn);

        /// <summary>
        /// Заполнить <paramref name="results"/> живыми юнитами в радиусе от <paramref name="center"/>.
        /// </summary>
        int QueryUnitsInRadius(
            Vector2 center,
            float radius,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam);

        /// <summary>
        /// Заполнить <paramref name="results"/> живыми юнитами в полосе из <paramref name="origin"/>
        /// вдоль <paramref name="direction"/>: длина <paramref name="length"/>, полная ширина
        /// <paramref name="width"/>. Опора линейной авто-атаки «Размашистый выпад» (шаг 4).
        /// </summary>
        int QueryUnitsInLine(
            Vector2 origin,
            Vector2 direction,
            float length,
            float width,
            List<RuntimeUnit> results,
            TargetFilter filter,
            int requestingTeam);

        /// <summary>Применить эффект к цели. Тело — Фаза 2; в Фазе 1 — стаб (no-op).</summary>
        void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source);

        /// <summary>
        /// То же наложение, но со сроком, посчитанным по ходу боя (в секундах, &gt; 0). Нужно там, где
        /// длительность — функция состояния цели, а не решение автора ассета: обездвиживание холодной
        /// линии растёт от 0.5 до 1.5 секунд вместе со стаками «Изморози». Заводить под каждую точку
        /// кривой свой ассет значило бы разложить одно число по трём файлам.
        /// </summary>
        void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds);

        /// <summary>
        /// Наложение с ВЕЛИЧИНОЙ, посчитанной накладывающим: <paramref name="potency"/> задаёт силу вместо
        /// авторской из ассета. Нужно порционным эффектам
        /// (<see cref="Data.Definitions.StackRule.Portions"/>): у кровотечения силу приносит удар, и она
        /// у каждой формы своя — поток отдаёт кровью весь урон, выпады добавляют её сверху долей.
        /// </summary>
        /// <remarks>
        /// Симметрична перегрузке со сроком выше и появилась по той же причине: величина бывает функцией
        /// состояния носителя, а не решением автора ассета. Разложить «сколько крови пускает этот кит» по
        /// отдельным ассетам крови значило бы развести ОДНУ линию на несколько владельцев силы, после чего
        /// правка линии перестала бы доходить до половины носителей.
        /// </remarks>
        void ApplyEffect(RuntimeUnit target, EffectData def, RuntimeUnit source, float durationSeconds, float potency);

        /// <summary>
        /// Сообщить презентации о сработавшей зоне удара (dev-оверлей зон, вики «13» шаг 4).
        /// Fire-and-forget — не мутирует симуляцию и не влияет на детерминизм.
        /// </summary>
        void ReportAreaHit(in AreaHit hit);

        /// <summary>Снять с цели подходящие эффекты (purge/cleanse, вики «6» §5.4).</summary>
        void Dispel(in Effects.DispelRequest req);

        /// <summary>
        /// Объявить, что <paramref name="caster"/> применил активную способность: событие
        /// <see cref="Effects.CombatEvent.AbilityCast"/> уходит всем его живым ВРАГАМ. На него реагирует
        /// «Отражающий налёт» Антимага — он копит щит за каждое вражеское заклинание.
        /// </summary>
        /// <remarks>
        /// Реакция висит на противнике, а не на кастующем, поэтому событие единственное в очереди, у
        /// которого носитель не один. Разослать его врагам — работа очереди, а не компонента: иначе
        /// каждый реактив сам обходил бы список юнитов, и порядок обхода стал бы частью исхода.
        /// </remarks>
        void ReportAbilityCast(RuntimeUnit caster);

        /// <summary>Принудительно сместить цель (отбрасывание/«ядро», §9.9). Исполняет DisplacementSystem.</summary>
        void Displace(in DisplaceRequest req);

        /// <summary>
        /// Призвать тело в бой (M10): собрать юнита из кита и поставить на поле. Возвращает призванного
        /// или <c>null</c>, если призывать нечем (фабрика не подана — так живут балансные бенчи).
        /// <para>Тело появляется по правилам спавна симуляции, то есть станет видимым в начале следующего
        /// тика. Раньше нельзя: список юнитов нельзя менять посреди обхода систем.</para>
        /// </summary>
        RuntimeUnit Summon(
            Data.Definitions.UnitData data, int team, UnityEngine.Vector2 position, RuntimeUnit summoner);

        /// <summary>
        /// Заявить мгновенный переход <paramref name="unit"/> за спину <paramref name="target"/>
        /// (§10.5 убийца, §10.6 монах). Не телепортирует на месте: заявки копятся и применяются все разом
        /// в конце раунда — от общего снимка позиций.
        /// </summary>
        /// <remarks>
        /// Телепорт посреди доставки событий двигал тело, из которого соседние реактивы того же раунда
        /// считали свою геометрию. На зеркале это ловилось так: два монаха заходят друг другу за спину,
        /// и тот, чей реактив сработал вторым, целится в уже сместившегося — стороны разъезжались на
        /// полшага (замер: X −8.20 против −8.70 вместо −8.20 против +8.20).
        /// </remarks>
        void TeleportBehind(RuntimeUnit unit, RuntimeUnit target);

        /// <summary>
        /// Юнит вошёл в замах авто-атаки (вики «14»): запускает анимацию свинга во View и «вжух»-SFX.
        /// Fire-and-forget — не мутирует симуляцию.
        /// </summary>
        void NotifyAttackStarted(RuntimeUnit unit, RuntimeUnit target);

        /// <summary>
        /// Замах авто-атаки прерван (стан/смерть себя): View рвёт свинг в idle, гасит звук замаха (вики «14»).
        /// Fire-and-forget — не мутирует симуляцию.
        /// </summary>
        void NotifyAttackInterrupted(RuntimeUnit unit);

        /// <summary>Генератор случайных чисел боя (детерминированный).</summary>
        IRngService Rng { get; }

        /// <summary>Текущий номер тика симуляции.</summary>
        int CurrentTick { get; }

        /// <summary>Константа K из StatsConfig для пайплайна брони.</summary>
        float ArmorK { get; }

        /// <summary>Снапшот балансного тюнинга, запечённый на старте боя (вики «13» §3.4, §4).</summary>
        SimTuning Tuning { get; }
    }
}
