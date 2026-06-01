using System;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Визуальный набор реликвии: спрайт-кадры по состояниям (Idle/Run/Attack/Death) + частота кадров.
    /// Прямые ссылки на нарезанные спрайты (вики «13» §2.6; Addressables + AnimatorOverrideController — Фаза 7).
    /// <para>
    /// Живёт в <c>Guildmaster.Data</c> (не Presentation), потому что сим/фабрика считают windup авто-атаки
    /// из <see cref="AttackHitFrame"/> + <see cref="AttackFrameCount"/> (вики «14»), а Combat видит только Core+Data.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Unit Visual", fileName = "UnitVisual")]
    public sealed class UnitVisual : ScriptableObject
    {
        [Tooltip("Кадров в секунду для не-атакующих клипов (Idle/Run/Death). Атака темп НЕ берёт отсюда — её задаёт windup (вики «14»).")]
        [SerializeField] private float _fps = 10f;

        [SerializeField] private Sprite[] _idle   = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _run    = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _attack = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _death  = Array.Empty<Sprite>();

        [Tooltip("Индексы кадров контакта авто-атаки (0-based). Рантайм Фазы 3 берёт [0]; массив — задел на мульти-удар (вики «14»). Клик по полоске кадров.")]
        [AttackHitFrameStrip]
        [SerializeField] private int[] _attackHitFrames = { 0 };

        public float Fps => _fps <= 0f ? 10f : _fps;

        /// <summary>Число кадров клипа атаки (= знаменатель в формуле windup).</summary>
        public int AttackFrameCount => _attack.Length;

        /// <summary>Кадр контакта для рантайма (первый элемент массива). 0 если не задан.</summary>
        public int AttackHitFrame => (_attackHitFrames != null && _attackHitFrames.Length > 0) ? _attackHitFrames[0] : 0;

        /// <summary>Все авторские кадры контакта (на будущее — мульти-удар). Может быть пустым.</summary>
        public int[] AttackHitFrames => _attackHitFrames;

        /// <summary>Кадры для состояния. Пустой массив = клип отсутствует (вид останется на текущем кадре).</summary>
        public Sprite[] Frames(UnitAnimationState state)
        {
            switch (state)
            {
                case UnitAnimationState.Run:    return _run;
                case UnitAnimationState.Attack: return _attack;
                case UnitAnimationState.Death:  return _death;
                default:                        return _idle;
            }
        }

        /// <summary>Длительность разового клипа (кадры / fps), сек. 0 если клип пуст. Для атаки не использовать — её темп задаёт windup.</summary>
        public float Duration(UnitAnimationState state)
        {
            Sprite[] frames = Frames(state);
            return frames.Length > 0 ? frames.Length / Fps : 0f;
        }
    }
}
