using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// v1-мозг (вики «13» §3.1, §4.2): интерпретирует <see cref="AIProfile"/>.
    /// Filter (живой / нужная команда) → Score (по <see cref="TargetingMode"/>) → Override
    /// (порог отступления → <see cref="PositioningIntent"/>). Тай-брейк при равном скоре —
    /// дистанция, затем <c>Id</c> (детерминизм, без RNG). Пишет только интент, мир не мутирует.
    /// </summary>
    public sealed class ProfileBrain : IUnitBrain
    {
        // Untagged-цель проигрывает любой тегнутой: штраф заведомо больше любого реального dist².
        private const float TaggedPenalty = 1_000_000_000f;

        private readonly AIProfile _profile;

        public ProfileBrain(AIProfile profile) => _profile = profile ?? new AIProfile();

        public void Decide(RuntimeUnit self, IBattleView view)
        {
            TargetingMode mode = _profile.AutoAttackTargeting;
            bool wantAllies = TargetsAllies(mode, _profile.AutoAttackMode);

            RuntimeUnit target = SelectBest(self, view.Units, mode, wantAllies);

            if (_profile.AutoAttackMode == AutoAttackMode.Heal)
            {
                // Хилер: авто-атака лечит союзника (AutoAttackTarget), а CurrentTarget держим на
                // ближайшем враге — для позиционирования/отступления (§2.7: «кого лечу» ≠ «от кого бегу»).
                self.AutoAttackTarget = target;
                self.CurrentTarget    = SelectBest(self, view.Units, TargetingMode.Nearest, wantAllies: false);
            }
            else
            {
                self.CurrentTarget    = target;
                self.AutoAttackTarget = target;
            }

            self.Positioning = ResolvePositioning(self);
        }

        // --- Filter ---

        private static bool TargetsAllies(TargetingMode mode, AutoAttackMode aaMode)
        {
            if (aaMode == AutoAttackMode.Heal) return true;
            return mode == TargetingMode.AllyNearest || mode == TargetingMode.AllyLowestHpPercent;
        }

        private RuntimeUnit SelectBest(RuntimeUnit self, IReadOnlyList<RuntimeUnit> units, TargetingMode mode, bool wantAllies)
        {
            RuntimeUnit best      = null;
            float       bestScore = float.MaxValue;
            float       bestDistSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit o = units[i];
                if (o.IsDead) continue;

                bool isAlly = o.Team == self.Team;
                if (wantAllies) { if (!isAlly || o == self) continue; }
                else            { if (isAlly) continue; }

                float distSq = (o.Position - self.Position).sqrMagnitude;
                float score  = Score(o, mode, distSq);

                bool better =
                    best == null
                    || score < bestScore
                    || (score == bestScore && distSq < bestDistSq)
                    || (score == bestScore && distSq == bestDistSq && o.Id < best.Id);

                if (better) { best = o; bestScore = score; bestDistSq = distSq; }
            }

            return best;
        }

        // --- Score (меньше = лучше, §4.2) ---

        private float Score(RuntimeUnit o, TargetingMode mode, float distSq)
        {
            switch (mode)
            {
                case TargetingMode.LowestHpFlat:        return o.CurrentHP;
                case TargetingMode.LowestHpPercent:
                case TargetingMode.AllyLowestHpPercent: return HpPct(o);
                case TargetingMode.HighestHp:           return -o.CurrentHP;
                case TargetingMode.HighestThreat:       return -EstimatedDps(o);
                case TargetingMode.PreferTagged:
                    bool tagged = (o.EffectTagMask & _profile.TargetTag) != 0;
                    return tagged ? distSq : distSq + TaggedPenalty;

                case TargetingMode.PreferUntagged:
                    // Зеркало PreferTagged: тегнутый штрафуется, нетегнутый выигрывает всегда
                    // (Криомант не добивает уже замороженного — распределяет «Заморозку» шире).
                    bool hasTag = (o.EffectTagMask & _profile.TargetTag) != 0;
                    return hasTag ? distSq + TaggedPenalty : distSq;

                case TargetingMode.Nearest:
                case TargetingMode.AllyNearest:
                default:
                    return distSq;
            }
        }

        private static float HpPct(RuntimeUnit u)
        {
            float maxHp = u.Stats.Get(StatType.MaxHP);
            return maxHp > 0f ? u.CurrentHP / maxHp : u.CurrentHP;
        }

        /// <summary>Оценочный DPS из статов — детерминированно, без истории урона (§2.1).</summary>
        private static float EstimatedDps(RuntimeUnit u)
        {
            return u.Stats.Get(StatType.AutoAttackDamage)
                 * u.Stats.Get(StatType.AttackSpeed)
                 * u.Stats.Get(StatType.DamageDealtEff);
        }

        // --- Override: позиционирование (§4.3) ---

        private PositioningIntent ResolvePositioning(RuntimeUnit self)
        {
            Retreat r = _profile.Retreat;
            if (r.Enabled)
            {
                float hp = HpPct(self);
                // Гистерезис (B > A): уже отступаем — продолжаем, пока не восстановились до ReturnAtHpPct;
                // иначе уходим в отступление лишь при падении ниже FleeAtHpPct. Зазор гасит дёрганье.
                if (self.Positioning == PositioningIntent.Retreat)
                {
                    if (hp < r.ReturnAtHpPct) return PositioningIntent.Retreat;
                }
                else if (hp <= r.FleeAtHpPct)
                {
                    return PositioningIntent.Retreat;
                }
            }

            if (_profile.Kite.Enabled) return PositioningIntent.Kite;
            return PositioningIntent.Approach;
        }
    }
}
