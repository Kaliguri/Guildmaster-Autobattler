using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Собиратель костей» Некроманта (карточка [[the-bonewright]]): пассивка НОСИТЕЛЯ, которая слышит
    /// смерть на арене и проверяет, не его ли это скелет. Свой призыв, погибший не дальше
    /// <see cref="_radius"/> от хозяина, превращается в кости: ближайший живой союзник получает
    /// <see cref="_boneShield"/>.
    /// <para><b>Числа:</b> <c>_radius</c> — на каком расстоянии от носителя смерть ещё перерабатывается
    /// (умер далеко — кости не донести); <c>_summonAbilityId</c> — сужение до одной способности, и в
    /// контенте оно ПУСТОЕ: кости даёт любой свой скелет, поднятый и активкой, и пассивкой (вердикт Макса
    /// 2026-07-30). Игрок скелетов не различает, и «этот даёт кости, а этот нет» читалось бы как дефект.
    /// Размер щита и сила его взрыва живут в самом эффекте костей.</para>
    /// <para><b>Когда срабатывает:</b> на <see cref="CombatEvent.UnitDied"/> — событие широковещательное,
    /// поэтому носитель узнаёт о смерти, в которой сам не участвовал.</para>
    /// </summary>
    /// <remarks>
    /// <b>Проверка «мой ли это скелет» живёт здесь, у Некроманта</b> (уточнение Макса 2026-07-30). Первая
    /// редакция ловила смерть любого юнита, вторая — вешала компонент на самого скелета, чтобы не менять
    /// доставку событий. Оба варианта хуже: первый дарил кости за чужие смерти, второй превращал
    /// способность хозяина в свойство призыва — тогда кит скелета знал бы про механику Некроманта, а его
    /// будущие улучшения («Костяная защита», «Лавина костей») не смогли бы её менять.
    /// <para><b>Радиус считается от носителя:</b> «донести кости» — про расстояние до хозяина, и падение
    /// скелета в дальнем углу арены ему ничего не даёт.</para>
    /// <para><b>Получатель — ближайший к трупу живой союзник</b> (им может оказаться и сам Некромант).
    /// Тай-брейк по <c>Id</c> сознательно не введён: при равных дистанциях он не зеркально-симметричен
    /// (открытый дефект в <c>ProfileBrain</c>), поэтому берётся первый ближайший в порядке запроса — он у
    /// отражённых сторон одинаков.</para>
    /// </remarks>
    [Serializable]
    public sealed class BoneCollectorComponent : IReactiveComponent
    {
        [Tooltip("Эффект костей: щит, который взрывается при пробитии (величина и взрыв — в нём).")]
        [SerializeField] private EffectData _boneShield;

        [Tooltip("Максимальное расстояние от НОСИТЕЛЯ, на котором смерть скелета ещё даёт кости. 0 = без ограничения.")]
        [SerializeField] private float _radius = 6f;

        [Tooltip("Id способности, чьи призывы считаются («summon_skeleton»). Пусто = любой свой призыв, " +
                 "включая выставленных в начале боя.")]
        [SerializeField] private string _summonAbilityId = "";

        [Tooltip("В каком радиусе от трупа искать получателя костей, мировые единицы.")]
        [SerializeField] private float _receiverSearchRadius = 10f;

        public CombatEvent Events => CombatEvent.UnitDied;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (_boneShield == null) return;

            RuntimeUnit self = ctx.Target;
            RuntimeUnit corpse = e.Target;
            if (self == null || self.IsDead || corpse == null || ReferenceEquals(corpse, self)) return;

            // Мой ли это призыв — и, если задано, от той ли способности.
            if (!ReferenceEquals(corpse.Summoner, self)) return;
            if (!string.IsNullOrEmpty(_summonAbilityId) && corpse.SummonAbilityId != _summonAbilityId) return;

            if (_radius > 0f
                && (corpse.Position - self.Position).sqrMagnitude > _radius * _radius) return;

            var allies = new List<RuntimeUnit>();
            ctx.Combat.QueryUnitsInRadius(
                corpse.Position, _receiverSearchRadius, allies, TargetFilter.Allies, self.Team);

            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < allies.Count; i++)
            {
                RuntimeUnit ally = allies[i];
                if (ally == null || ally.IsDead) continue;

                float sq = (ally.Position - corpse.Position).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = ally;
            }

            if (best != null) ctx.Combat.ApplyEffect(best, _boneShield, self);
        }
    }
}
