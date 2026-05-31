using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Спайк S4 (вики «13» §5): доказательство развязки анимации от сима на уровне логики.
    /// <see cref="UnitAnimationSelector.Select"/> — чистая функция наблюдаемого состояния сима
    /// (мертвость, движение) + таймеров разовых клипов от событий тика. Одинаковый ввод →
    /// одинаковое состояние анимации на любом клиенте; функция ничего не возвращает в сим.
    /// </summary>
    public sealed class UnitAnimationSelectorTests
    {
        [Test]
        public void Select_Dead_AlwaysDeath_OverridingEverything()
        {
            // Даже при активных hit/attack/движении смерть латчит на Death.
            var s = UnitAnimationSelector.Select(isDead: true, hitRemaining: 0.5f, attackRemaining: 0.5f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Death, s);
        }

        [Test]
        public void Select_Attack_NotInterruptedByHit()
        {
            // Флинч от урона НЕ перебивает текущий замах атаки (Attack > Hit, коммит f00a01e).
            // Hit-анимация второстепенна — в будущем, вероятно, заменится hit-VFX (белый флеш).
            var s = UnitAnimationSelector.Select(isDead: false, hitRemaining: 0.2f, attackRemaining: 0.3f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Attack, s);
        }

        [Test]
        public void Select_Hit_WhenNotAttacking_OverridesMovement()
        {
            // Флинч играет, только если нет активной атаки; тогда перекрывает движение.
            var s = UnitAnimationSelector.Select(isDead: false, hitRemaining: 0.2f, attackRemaining: 0f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Hit, s);
        }

        [Test]
        public void Select_Attack_OverridesMovement()
        {
            var s = UnitAnimationSelector.Select(isDead: false, hitRemaining: 0f, attackRemaining: 0.3f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Attack, s);
        }

        [Test]
        public void Select_Moving_IsRun()
        {
            var s = UnitAnimationSelector.Select(isDead: false, hitRemaining: 0f, attackRemaining: 0f, isMoving: true);
            Assert.AreEqual(UnitAnimationState.Run, s);
        }

        [Test]
        public void Select_StillAndIdle_IsIdle()
        {
            var s = UnitAnimationSelector.Select(isDead: false, hitRemaining: 0f, attackRemaining: 0f, isMoving: false);
            Assert.AreEqual(UnitAnimationState.Idle, s);
        }

        [Test]
        public void Select_Deterministic_SameInputSameOutput()
        {
            // Тот же ввод дважды → тот же результат (нет скрытого стейта/wall-clock) — основа ко-оп консистентности.
            var a = UnitAnimationSelector.Select(false, 0f, 0.1f, true);
            var b = UnitAnimationSelector.Select(false, 0f, 0.1f, true);
            Assert.AreEqual(a, b);
        }
    }
}
