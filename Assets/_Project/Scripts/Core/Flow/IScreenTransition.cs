using System;
using UnityEngine;

namespace Guildmaster.Core.Flow
{
    /// <summary>
    /// Форма одного «моргания»: сколько закрываемся, сколько держим закрытый кадр, сколько открываемся,
    /// и куда кадр схлопывается. Ритм приносит ЗАКАЗЧИК перехода (у карты он свой, в её стиле), а ведёт
    /// его <see cref="IScreenTransition"/> — сам заказчик до конца перехода не доживает.
    /// </summary>
    public readonly struct ScreenTransitionShape
    {
        /// <summary>Закрытие кадра, секунды.</summary>
        public readonly float InSeconds;

        /// <summary>Выдержка на закрытом кадре, секунды. Именно она читается как «моргнули».</summary>
        public readonly float HoldSeconds;

        /// <summary>Открытие уже нового кадра, секунды.</summary>
        public readonly float OutSeconds;

        /// <summary>
        /// Куда схлопывается кадр в момент клика, в долях экрана. Пока идёт закрытие, точка едет отсюда
        /// к центру экрана: узел одновременно приближается камерой, и вдвоём это читается как «ныряем в него».
        /// </summary>
        public readonly Vector2 FocusUv;

        /// <summary>
        /// Насколько поздно вступают чернила, в долях фазы закрытия (0 — вместе с движением, 0.5 — на его
        /// середине). Движение заказчика идёт с самого начала, а шторка догоняет: сперва видно, КУДА
        /// ныряем, и лишь потом кадр затягивает. Совпадающие старты съедали весь нырок под чернилами.
        /// </summary>
        public readonly float InkDelay01;

        public ScreenTransitionShape(float inSeconds, float holdSeconds, float outSeconds, Vector2 focusUv,
                                     float inkDelay01 = 0f)
        {
            InSeconds   = Mathf.Max(0.01f, inSeconds);
            HoldSeconds = Mathf.Max(0f,    holdSeconds);
            OutSeconds  = Mathf.Max(0.01f, outSeconds);
            FocusUv     = focusUv;
            InkDelay01  = Mathf.Clamp(inkDelay01, 0f, 0.9f);
        }

    }

    /// <summary>
    /// Владелец перехода между кадрами: закрыть экран → на закрытом отдать управление заказчику (подменить
    /// то, что под шторкой) → открыть.
    /// </summary>
    /// <remarks>
    /// Живёт ОТДЕЛЬНО от того, кто переход заказал, и это главное в нём (QA #53). Раньше три фазы вела карта
    /// акта, но выбор узла, засчитанный на закрытом кадре, уводит игрока с карты — карта скрывалась в
    /// середине собственного перехода и обрывала его на пике: игрок видел только закрытие, а выдержку и
    /// открытие не видел никогда. Владелец шторки обязан пережить смену того, что под шторкой.
    /// </remarks>
    public interface IScreenTransition
    {
        /// <summary>Идёт ли переход прямо сейчас. Пока идёт — новый заказ игнорируется.</summary>
        bool Busy { get; }

        /// <summary>
        /// Сыграть моргание.
        /// </summary>
        /// <param name="shape">Ритм и точка схлопывания.</param>
        /// <param name="onClosing">
        /// Ход закрытия, 0..1. Зовётся каждый кадр фазы закрытия — сюда заказчик вешает то, что должно
        /// двигаться ВМЕСТЕ со шторкой (наезд камеры на узел). Может быть null.
        /// </param>
        /// <param name="onCovered">
        /// Кадр закрыт наглухо: пора подменять то, что под ним (засчитать выбор, уйти в узел). Может быть null.
        /// </param>
        void Play(in ScreenTransitionShape shape, Action<float> onClosing, Action onCovered);

        /// <summary>
        /// Оборвать переход и открыть кадр немедленно. Для выхода из забега: держать чернила на экране,
        /// когда мира под ними уже нет, нельзя.
        /// </summary>
        void Cancel();
    }
}
