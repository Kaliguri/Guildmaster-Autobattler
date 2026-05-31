using System;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Визуальный набор реликвии для харнесса Фазы 3: спрайт-кадры по состояниям
    /// (Idle/Run/Attack/Hit/Death) + частота кадров. Прямые ссылки на нарезанные
    /// спрайты (вики «13» §2.6; полноценный Addressables + AnimatorOverrideController — Фаза 7).
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Presentation/Unit Visual", fileName = "UnitVisual")]
    public sealed class UnitVisual : ScriptableObject
    {
        [Tooltip("Кадров в секунду для всех клипов (рендер-время, развязано с сим-тиком).")]
        [SerializeField] private float _fps = 10f;

        [SerializeField] private Sprite[] _idle   = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _run    = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _attack = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _hit    = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _death  = Array.Empty<Sprite>();

        public float Fps => _fps <= 0f ? 10f : _fps;

        /// <summary>Кадры для состояния. Пустой массив = клип отсутствует (вид останется на текущем кадре).</summary>
        public Sprite[] Frames(UnitAnimationState state)
        {
            switch (state)
            {
                case UnitAnimationState.Run:    return _run;
                case UnitAnimationState.Attack: return _attack;
                case UnitAnimationState.Hit:    return _hit;
                case UnitAnimationState.Death:  return _death;
                default:                        return _idle;
            }
        }

        /// <summary>Длительность разового клипа (кадры / fps), сек. 0 если клип пуст.</summary>
        public float Duration(UnitAnimationState state)
        {
            Sprite[] frames = Frames(state);
            return frames.Length > 0 ? frames.Length / Fps : 0f;
        }
    }
}
