using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Самоинъекция дев-панелей: дождаться, когда нужный скоуп будет ПОСТРОЕН, и влить зависимости.
    /// </summary>
    /// <remarks>
    /// <b>Существует из-за гонки, которая выглядит как случайный NRE на старте.</b> Дев-панели лежат в
    /// аддитивной сцене и делают самоинъекцию сами (DevTools знает Game, обратного нет). Прежний
    /// однострочник <c>scope?.Container.Inject(this)</c> проверял на null только САМ СКОУП, а падал на
    /// <c>Container</c>: объект скоупа в сцене уже есть, а контейнер он строит позже — и чей <c>Start</c>
    /// случится раньше, зависит от порядка объектов, то есть меняется от любой правки сцены.
    /// <para>Ждём кадрами, а не подписываемся на событие: у <c>LifetimeScope</c> нет публичного «я
    /// построился», а дев-инструмент вправе стоить один <c>if</c> в кадр на старте сцены.</para>
    /// </remarks>
    public static class DevSelfInject
    {
        /// <summary>Сколько кадров ждать построения скоупа, прежде чем сдаться с предупреждением.</summary>
        private const int MaxFrames = 120;

        /// <summary>
        /// Влить зависимости в <paramref name="target"/> из скоупа <typeparamref name="TScope"/>, как только
        /// тот построится. Если скоупа нет вовсе (standalone-сцена без бута) — тихо выходим: для дев-панели
        /// это «нечего показывать», а не ошибка.
        /// </summary>
        public static async UniTaskVoid WhenScopeReady<TScope>(MonoBehaviour target, Action onInjected = null)
            where TScope : LifetimeScope
        {
            if (target == null) return;

            for (int frame = 0; frame < MaxFrames; frame++)
            {
                if (target == null) return;   // панель могли снести, пока ждали

                LifetimeScope scope = LifetimeScope.Find<TScope>();
                if (scope != null && scope.Container != null)
                {
                    scope.Container.Inject(target);
                    onInjected?.Invoke();
                    return;
                }

                await UniTask.NextFrame();
            }

            Debug.LogWarning($"[DevSelfInject] - {typeof(TScope).Name} не построился за {MaxFrames} кадров: " +
                             $"{target.GetType().Name} остался без зависимостей.", target);
        }
    }
}
