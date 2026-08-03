using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Лента событий одного боя: кто, когда, что и кому сделал — построчно, в порядке случившегося.
    /// </summary>
    /// <remarks>
    /// <para>Зачем: агрегатные метрики отвечают на «сколько», но не на «почему». Когда кит льёт 88 секунд
    /// контроля и всё равно проигрывает, из таблицы этого не понять — нужен один бой, разобранный по
    /// событиям. До сих пор этот вопрос закрывался только глазами в play-mode.</para>
    /// <para>Подписывается на те же outward-события сима и системы эффектов, что и
    /// <see cref="MetricCollector"/>, и так же ничего не пересчитывает: в ленте стоят фактические
    /// значения <see cref="DamageResult"/>, иначе лента врала бы убедительнее таблицы.</para>
    /// <para>Юниты ищутся ПО ССЫЛКЕ, а не по Id: Id раздаёт <see cref="SimBench.Drive"/> уже после того,
    /// как писарь подписан, и запоминать их заранее нельзя.</para>
    /// </remarks>
    internal sealed class BattleTrace
    {
        public static readonly string[] Headers = { "Сек", "Кто", "Действие", "Кому", "Сколько", "Чем", "HP цели" };

        /// <summary>Потолок строк: долгий бой на площади даёт десятки тысяч событий, и такой файл не читают.</summary>
        private const int DefaultMaxRows = 2500;

        private readonly CombatSimulation _sim;
        private readonly IReadOnlyList<TrackedUnit> _tracked;
        private readonly List<IReadOnlyList<object>> _rows = new List<IReadOnlyList<object>>();
        private readonly int _maxRows;

        /// <summary>Лента оборвана по потолку строк — про это обязан сказать отчёт, а не догадываться читатель.</summary>
        public bool Truncated { get; private set; }

        public IReadOnlyList<IReadOnlyList<object>> Rows => _rows;

        public BattleTrace(SimEnvironment env, IReadOnlyList<TrackedUnit> tracked, int maxRows = DefaultMaxRows)
        {
            _sim = env.Sim;
            _tracked = tracked;
            _maxRows = maxRows;

            env.Sim.OnDamageDealt += HandleDamage;
            env.Sim.OnHealed += HandleHeal;
            env.Sim.OnUnitDied += HandleDeath;
            env.Sim.OnAttackEvaded += HandleEvaded;

            env.Effects.OnEffectApplied += HandleEffectApplied;
            env.Effects.OnEffectEnded += HandleEffectEnded;
            env.Effects.OnEffectDispelled += HandleDispelled;
        }

        private void HandleDamage(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            string how = result.SourceKind.ToString().ToLowerInvariant() + ", " +
                         (result.School == DamageSchool.Magical ? "маг" : "физ");
            if (result.ShieldDamage > 0.001f) how += $", в щит {result.ShieldDamage:0}";
            if (result.Mitigated > 0.001f) how += $", срезано {result.Mitigated:0}";
            if (result.KilledTarget) how += ", СМЕРТЕЛЬНЫЙ";

            bool self = source != null && ReferenceEquals(source, target);
            Add(Name(source), self ? "самоурон" : "урон", Name(target), result.TotalDamage, how, Hp(target));
        }

        private void HandleHeal(RuntimeUnit source, RuntimeUnit target, float amount)
            => Add(Name(source), ReferenceEquals(source, target) ? "самолечение" : "лечение",
                Name(target), amount, "", Hp(target));

        private void HandleEvaded(RuntimeUnit target)
            => Add(Name(target), "уклонился", "", null, "входящий удар отменён целиком", Hp(target));

        private void HandleDeath(RuntimeUnit unit)
            => Add(Name(unit), "погиб", "", null, "", "0");

        private void HandleEffectApplied(RuntimeUnit target, EffectData def, RuntimeUnit source)
        {
            if (def == null) return;

            // Длительность — через ту же функцию, что считает её в бою: сопротивление режет реальный срок,
            // и «наложил 4 с» из ассета оказалось бы длиннее случившегося.
            int ticks = EffectSystem.ResolveDurationTicks(def, source, target);
            object seconds = ticks > 0 ? (object)(ticks / (double)SimConstants.TickRate) : null;
            string how = def.name + (ticks > 0 ? " с" : " (постоянный)");
            Add(Name(source), def.Polarity == EffectPolarity.Debuff ? "наложил дебафф" : "наложил бафф",
                Name(target), seconds, how, Hp(target));
        }

        private void HandleEffectEnded(RuntimeUnit target, EffectData def, RuntimeUnit source)
            => Add(Name(source), "эффект кончился", Name(target), null, def != null ? def.name : "", Hp(target));

        private void HandleDispelled(RuntimeUnit target, EffectData def, RuntimeUnit dispeller, RuntimeUnit caster)
            => Add(Name(dispeller), "снял эффект", Name(target), null,
                (def != null ? def.name : "") + " (наложил " + Name(caster) + ")", Hp(target));

        private void Add(string actor, string action, string target, object amount, string how, string hp)
        {
            if (_rows.Count >= _maxRows)
            {
                Truncated = true;
                return;
            }

            _rows.Add(new object[]
            {
                _sim.CurrentTick / (double)SimConstants.TickRate,
                actor, action, target, amount, how, hp,
            });
        }

        private string Name(RuntimeUnit unit)
        {
            if (unit == null) return "-";
            for (int i = 0; i < _tracked.Count; i++)
                if (ReferenceEquals(_tracked[i].Unit, unit)) return _tracked[i].Label;

            // Юнит, которого бенч не заводил: призванный питомец или что-то ещё, появившееся в бою.
            return "id" + unit.Id;
        }

        private static string Hp(RuntimeUnit unit)
        {
            if (unit == null) return "";
            float max = unit.Stats != null ? unit.Stats.Get(StatType.MaxHP) : 0f;
            return $"{(unit.CurrentHP > 0f ? unit.CurrentHP : 0f):0}/{max:0}";
        }
    }
}
