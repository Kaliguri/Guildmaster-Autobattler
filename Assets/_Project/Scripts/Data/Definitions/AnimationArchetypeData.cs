using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// АРХЕТИП АНИМАЦИЙ: набор клипов на <see cref="AnimationClip"/>-слотах — Idle/Run/Attack/Death/Hit,
    /// клипы скиллов, портрет. Один архетип = одна хореография; «меч и щит», «копьё», «посох» будут
    /// разными архетипами при общем скелете.
    /// <para>
    /// <b>Назывался <c>UnitVisual</c> до 06.08.2026, и имя врало:</b> визуал — это спрайты, а здесь клипы.
    /// Переименовано по заказу Макса вместе с остальным неймингом фазы 0
    /// (<c>tech/40-planning/weapon-system</c> §6.1).
    /// </para>
    /// <para>
    /// <b>Клипы отсюда НЕ подменяются override-контроллером</b>, что бы ни говорил прежний докстринг:
    /// <c>UnitView</c> играет стейты контроллера по имени (<c>Animator.Play</c>), а этот набор служит
    /// источником МАРКЕРОВ и темпа. Сборка <c>AnimatorOverrideController</c> жила в покадровом
    /// пайплайне и удалена вместе с ним.
    /// </para>
    /// <para>
    /// Живёт в <c>Guildmaster.Data</c> (не Presentation), потому что сим/фабрика считают windup авто-атаки
    /// из <see cref="AttackFrameCount"/> + <see cref="AttackHitFrame"/>, а Combat видит только Core+Data.
    /// Эти числа выводятся из Attack-клипа и его маркера (<see cref="ClipMarkers"/>). <b>Это и есть та
    /// зависимость, которую фаза 2 плана снимает:</b> тайминг переедет в объявленные доли, а клип
    /// останется показом.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Animation Archetype", fileName = "AnimationArchetype")]
    public sealed class AnimationArchetypeData : ScriptableObject
    {
        [Header("Тело")]
        [Tooltip("Префаб вида: риг с нужными узлами и контроллером. Живёт ЗДЕСЬ, а не у юнита, потому что " +
                 "риг и набор клипов — одна вещь: у копья свои узлы И свои клипы, и порознь они бессмысленны.")]
        [SerializeField] private GameObject _viewPrefab;

        [Header("Animation clips")]
        [SerializeField] private AnimationClip _idleClip;
        [SerializeField] private AnimationClip _runClip;
        [Tooltip("Клип авто-атаки. Должен нести маркер контакта (AnimationEvent \"Marker\") — с него сим берёт windup.")]
        [SerializeField] private AnimationClip _attackClip;
        [SerializeField] private AnimationClip _deathClip;
        [Tooltip("Разовая реакция на урон (косметика). Может быть пустой.")]
        [SerializeField] private AnimationClip _hitClip;
        [Tooltip("Клип гвардии — тот же, что лежит в стейте слоя «Block». Показ читает из него маркеры " +
                 "GuardUp/GuardDown: где щит встал и где пошёл вниз. Пусто — гвардия играется целиком, " +
                 "как одна поза. Может быть пустым у китов без щита.")]
        [SerializeField] private AnimationClip _guardClip;
        [Tooltip("Клипы кастов по слотам скиллов (индекс = SkillSlot).")]
        [SerializeField] private AnimationClip[] _skillClips = new AnimationClip[4];
        [Tooltip("Портрет для HUD/тултипов. Опционален.")]
        [SerializeField] private Sprite _portrait;

        /// <summary>
        /// Префаб вида этого архетипа: риг с его узлами, Animator с его контроллером. Юнит берёт вид
        /// отсюда и своего поля под него не имеет — иначе архетип «копьё» уживался бы в данных с
        /// префабом меча, и юнит выходил бы на арену махать клипами копья по мечу.
        /// </summary>
        public GameObject ViewPrefab => _viewPrefab;

        /// <summary>Портрет для HUD/тултипов (может быть <c>null</c>).</summary>
        public Sprite Portrait => _portrait;

        /// <summary>Клип авто-атаки (UnitView читает его маркер для подгонки скорости под windup).</summary>
        public AnimationClip AttackClip => _attackClip;

        /// <summary>Клип базового состояния (Idle/Run/Attack/Death). <c>null</c> = слот не задан.</summary>
        public AnimationClip Clip(UnitAnimationState state)
        {
            switch (state)
            {
                case UnitAnimationState.Run:    return _runClip;
                case UnitAnimationState.Attack: return _attackClip;
                case UnitAnimationState.Death:  return _deathClip;
                default:                        return _idleClip;
            }
        }

        /// <summary>Клип разовой реакции на урон (может быть <c>null</c>).</summary>
        public AnimationClip HitClip => _hitClip;

        /// <summary>
        /// Клип гвардии — источник маркеров <see cref="ClipMarkers.GuardUpFunction"/> и
        /// <see cref="ClipMarkers.GuardDownFunction"/>. <c>null</c> = кит без щита или клип не разведён:
        /// тогда показ играет позу целиком, без фаз.
        /// </summary>
        public AnimationClip GuardClip => _guardClip;

        /// <summary>Клип каста по слоту скилла (0-based). <c>null</c> = слот пуст или индекс вне диапазона.</summary>
        public AnimationClip SkillClip(int slot) =>
            _skillClips != null && slot >= 0 && slot < _skillClips.Length ? _skillClips[slot] : null;

        /// <summary>Есть ли Attack-клип — признак, что визуал готов к проигрыванию через Animator.</summary>

        /// <summary>Число кадров Attack-клипа (знаменатель windup, вики «14»). 0 если клипа нет.</summary>
        public int AttackFrameCount => ClipMarkers.FrameCount(_attackClip);

        /// <summary>Кадр контакта авто-атаки = маркер Attack-клипа (числитель windup). 0 если нет клипа/маркера.</summary>
        public int AttackHitFrame => ClipMarkers.HitFrame(_attackClip);

        /// <summary>
        /// Сколько Ударов в одной Атаке: столько, сколько маркеров в клипе. <c>0</c> = разметки нет.
        /// </summary>
        public int AttackHitCount => ClipMarkers.HitCount(_attackClip);

        /// <summary>
        /// Нормированные позиции всех контактов (0..1) в порядке ударов — дописываются в
        /// <paramref name="result"/>, возвращается их число. Тики из них считает <c>AttackTiming</c>:
        /// доли живут в клипе, а перевод в тики зависит от рантайм-скорости атаки.
        /// </summary>
        public int AttackHitPositions(System.Collections.Generic.List<float> result) =>
            ClipMarkers.HitNormalizedAll(_attackClip, result);
    }
}
