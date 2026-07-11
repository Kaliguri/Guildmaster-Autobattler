using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Визуальный набор реликвии на <see cref="AnimationClip"/>-слотах (вики «13» §3.1): состояния
    /// Idle/Run/Attack/Death/Hit + клипы скиллов + портрет. <c>UnitView</c> собирает из них
    /// <c>AnimatorOverrideController</c> поверх базового контроллера.
    /// <para>
    /// Живёт в <c>Guildmaster.Data</c> (не Presentation), потому что сим/фабрика считают windup авто-атаки
    /// из <see cref="AttackFrameCount"/> + <see cref="AttackHitFrame"/> (вики «14»), а Combat видит только
    /// Core+Data. Эти числа выводятся из Attack-клипа и его маркера (<see cref="ClipMarkers"/>) —
    /// клип единственный источник правды, без дублирующих полей.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Unit Visual", fileName = "UnitVisual")]
    public sealed class UnitVisual : ScriptableObject
    {
        [Header("Animation clips")]
        [SerializeField] private AnimationClip _idleClip;
        [SerializeField] private AnimationClip _runClip;
        [Tooltip("Клип авто-атаки. Должен нести маркер контакта (AnimationEvent \"Marker\") — с него сим берёт windup.")]
        [SerializeField] private AnimationClip _attackClip;
        [SerializeField] private AnimationClip _deathClip;
        [Tooltip("Разовая реакция на урон (косметика). Может быть пустой.")]
        [SerializeField] private AnimationClip _hitClip;
        [Tooltip("Клипы кастов по слотам скиллов (индекс = SkillSlot).")]
        [SerializeField] private AnimationClip[] _skillClips = new AnimationClip[4];
        [Tooltip("Портрет для HUD/тултипов. Опционален.")]
        [SerializeField] private Sprite _portrait;

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

        /// <summary>Клип каста по слоту скилла (0-based). <c>null</c> = слот пуст или индекс вне диапазона.</summary>
        public AnimationClip SkillClip(int slot) =>
            _skillClips != null && slot >= 0 && slot < _skillClips.Length ? _skillClips[slot] : null;

        /// <summary>Есть ли Attack-клип — признак, что визуал готов к проигрыванию через Animator.</summary>
        public bool HasClips => _attackClip != null;

        /// <summary>Число кадров Attack-клипа (знаменатель windup, вики «14»). 0 если клипа нет.</summary>
        public int AttackFrameCount => ClipMarkers.FrameCount(_attackClip);

        /// <summary>Кадр контакта авто-атаки = маркер Attack-клипа (числитель windup). 0 если нет клипа/маркера.</summary>
        public int AttackHitFrame => ClipMarkers.HitFrame(_attackClip);
    }
}
