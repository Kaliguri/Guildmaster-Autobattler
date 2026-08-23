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
            // Форма сильнее кита: у бойца со стойками фокус свой в каждой (Десятина вдали ищет самого
            // живучего — тот дольше кровоточит, в упор бьёт ближайшего). Пусто — стоек на бойце нет, и
            // работает профиль кита.
            TargetingMode mode = self.StanceTargeting ?? _profile.AutoAttackTargeting;
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

            self.Positioning = ResolvePositioning(self, view);
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

                // Маскировка: скрытый враг невидим для вражеского таргетинга — его нельзя выбрать целью
                // (враги не бегут к нему и не бьют), пока его не заметили с близкой дистанции или он сам
                // себя не выдал ударом. Единственная точка выбора цели во всём бою — поэтому фильтр
                // видимости стоит здесь и больше нигде. Союзный таргетинг (хил) маскировка не блокирует.
                //
                // Судим по состоянию, а не по тегу эффекта (до 2026-07-31 здесь стояла проверка
                // EffectTag.Stealth): у Маскировки четыре ступени, и «висит тег» больше не значит
                // «не видно» — видно или нет, решает ConcealmentSystem по расстоянию.
                if (!wantAllies && o.IsHidden) continue;

                // Сон («Колыбельная», [[the-lull]]): спящий выпадает из автовыбора ВСЕЙ команды —
                // правило агро, а не свойство эффекта, иначе каждого кита пришлось бы настраивать
                // руками. Исключение — тот, кто спящих ищет намеренно (демон добивает свою цель):
                // он объявляет это профилем, предпочитая тег сна.
                if (!wantAllies && (o.EffectTagMask & EffectTag.Sleep) != 0 && !HuntsSleepers(mode)) continue;

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

        /// <summary>
        /// Охотится ли носитель профиля за спящими: только если он ЯВНО предпочитает тег сна
        /// (<see cref="TargetingMode.PreferTagged"/> + <c>TargetTag = Sleep</c>). Так исключение из общего
        /// правила агро объявляется в данных кита, а не зашивается в код по имени мементо.
        /// </summary>
        /// <param name="mode">
        /// ДЕЙСТВУЮЩИЙ режим, а не профильный: у бойца со стойками фокус переписывает форма, и правило
        /// «спящих не выбираем» обязано смотреть на тот же режим, что и выбор цели.
        /// </param>
        private bool HuntsSleepers(TargetingMode mode) =>
            mode == TargetingMode.PreferTagged
            && (_profile.TargetTag & EffectTag.Sleep) != 0;

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
                    // Учитываем и уже наложенный тег, и ЛЕТЯЩИЙ в цель (входящая бронь снаряда).
                    bool tagged = ((o.EffectTagMask | o.IncomingEffectTags) & _profile.TargetTag) != 0;
                    return tagged ? distSq : distSq + TaggedPenalty;

                case TargetingMode.PreferUntagged:
                    // Зеркало PreferTagged: тегнутый штрафуется, нетегнутый выигрывает всегда
                    // (Криомант не добивает уже замороженного — распределяет «Заморозку» шире). «Тегнут» =
                    // эффект уже висит ИЛИ снаряд с ним уже летит (IncomingEffectTags) → не шлём вторую «Заморозку».
                    bool hasTag = ((o.EffectTagMask | o.IncomingEffectTags) & _profile.TargetTag) != 0;
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

        private PositioningIntent ResolvePositioning(RuntimeUnit self, IBattleView view)
        {
            Retreat r = _profile.Retreat;
            if (r.Enabled)
            {
                float hp = HpPct(self);

                // Оправился — отступление снова доступно целиком. Сброс идёт по тому же порогу, по
                // которому боец возвращается в бой: у «побега хватило» и «побег закончился» одно условие.
                if (hp >= r.ReturnAtHpPct) self.RetreatTicks = 0;

                // Предел отступления: порог возврата задан долей HP, а поднять её нечем, когда лечить
                // некому. Без предела боец уходит навсегда, обе стороны живы, и бой не разрешается —
                // овертайм тут бессилен, он умножает урон, которого нет. Кайтера предел не касается:
                // отход и есть его способ драться.
                bool spent = !_profile.Kite.Enabled
                             && self.RetreatTicks >= RetreatCapTicks(view);

                if (!spent)
                {
                    // Гистерезис (B > A): уже отступаем — продолжаем, пока не восстановились до
                    // ReturnAtHpPct; иначе уходим в отступление лишь при падении ниже FleeAtHpPct.
                    // Зазор гасит дёрганье.
                    if (self.Positioning == PositioningIntent.Retreat)
                    {
                        if (hp < r.ReturnAtHpPct) { self.RetreatTicks++; return PositioningIntent.Retreat; }
                    }
                    else if (hp <= r.FleeAtHpPct)
                    {
                        self.RetreatTicks++;
                        return PositioningIntent.Retreat;
                    }
                }
            }

            if (_profile.Kite.Enabled) return PositioningIntent.Kite;
            return PositioningIntent.Approach;
        }

        /// <summary>Предел отступления в тиках мозга. Мозг решает не каждый тик — считаем по его каденсу.</summary>
        private static int RetreatCapTicks(IBattleView view)
        {
            float seconds = view.Tuning.RetreatMaxSeconds;
            if (seconds <= 0f) return int.MaxValue;   // ноль = предела нет (прежнее поведение)
            int ticks = (int)(seconds * Core.Simulation.SimConstants.TickRate);
            return ticks < 1 ? 1 : ticks;
        }
    }
}
