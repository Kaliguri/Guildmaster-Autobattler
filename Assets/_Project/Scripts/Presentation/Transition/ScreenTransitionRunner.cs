using System;
using Guildmaster.Core.Flow;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Presentation.Transition
{
    /// <summary>
    /// Владелец «моргания» между кадрами: ведёт три фазы (закрыть → выдержать → открыть) и на закрытом кадре
    /// отдаёт управление заказчику. Плотность и точку схлопывания вещает наружу
    /// (<see cref="ScreenFadeChangedEvent"/>) — рисует шторку UI-слой, поверх всех камер и панелей.
    /// </summary>
    /// <remarks>
    /// Не MonoBehaviour и не житель сцены: переход обязан пережить и смену сцены под собой, и уход
    /// заказчика. Тикает от корневого скоупа по НЕмасштабированному времени — пауза боя и хрономант
    /// на смену кадра влиять не должны.
    /// </remarks>
    public sealed class ScreenTransitionRunner : IScreenTransition, ITickable
    {
        private enum Stage { None, In, Hold, Out }

        private static readonly Vector2 ScreenCenter = new Vector2(0.5f, 0.5f);

        private readonly IPublisher<ScreenFadeChangedEvent> _fadePub;

        private Stage _stage;
        private float _time;
        private ScreenTransitionShape _shape;
        private Action<float> _onClosing;
        private Action _onCovered;

        public ScreenTransitionRunner(IPublisher<ScreenFadeChangedEvent> fadePub) => _fadePub = fadePub;

        /// <inheritdoc/>
        public bool Busy => _stage != Stage.None;

        /// <inheritdoc/>
        public void Play(in ScreenTransitionShape shape, Action<float> onClosing, Action onCovered)
        {
            // Второй заказ поверх идущего перехода — это два хозяина у одной шторки. Побеждает первый:
            // он уже ведёт игрока куда-то, и перебивать его на полпути значит показать рывок.
            if (Busy) return;

            _shape     = shape;
            _onClosing = onClosing;
            _onCovered = onCovered;
            _stage     = Stage.In;
            _time      = 0f;

            Publish(0f);
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            if (!Busy) return;

            _stage     = Stage.None;
            _onClosing = null;
            _onCovered = null;
            Publish(0f);
        }

        /// <inheritdoc/>
        public void Tick() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// Шаг перехода с ЯВНОЙ длительностью кадра. Отдельно от <see cref="Tick()"/>, чтобы ход перехода
        /// можно было проверить тестами: время — единственное, что у него снаружи.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (!Busy) return;

            _time += deltaSeconds;

            switch (_stage)
            {
                case Stage.In:
                {
                    if (_time < _shape.InSeconds)
                    {
                        float t = Closing(_time / _shape.InSeconds);
                        Publish(t);
                        _onClosing?.Invoke(t);
                        break;
                    }

                    Publish(1f);
                    _onClosing?.Invoke(1f);
                    _onClosing = null;

                    // Подмену делаем на ЗАКРЫТОМ кадре и до перехода в выдержку: заказчик волен внутри
                    // колбэка снести всё, что было под шторкой, — на ходе перехода это уже не скажется.
                    _stage = Stage.Hold;
                    _time  = 0f;

                    Action covered = _onCovered;
                    _onCovered = null;
                    covered?.Invoke();
                    break;
                }

                case Stage.Hold:
                {
                    if (_time < _shape.HoldSeconds) break;
                    _stage = Stage.Out;
                    _time  = 0f;
                    break;
                }

                case Stage.Out:
                {
                    if (_time < _shape.OutSeconds)
                    {
                        Publish(1f - Opening(_time / _shape.OutSeconds));
                        break;
                    }

                    _stage = Stage.None;
                    Publish(0f);
                    break;
                }
            }
        }

        // Закрытие идёт С УСКОРЕНИЕМ: начало мягкое, конец резкий — так это читается как рывок внутрь,
        // а не как ровное затухание света.
        private static float Closing(float t) => t * t;

        // Открытие, наоборот, тормозит к концу: новый кадр не выпрыгивает, а проявляется.
        private static float Opening(float t) => 1f - (1f - t) * (1f - t);

        private void Publish(float progress)
        {
            // Точка схлопывания едет к центру экрана вместе с закрытием: узел в это же время приближается
            // камерой, и вдвоём это читается как вход в узел, а не как затемнение около него.
            Vector2 center = Vector2.Lerp(_shape.FocusUv, ScreenCenter, Mathf.Clamp01(progress));
            _fadePub?.Publish(new ScreenFadeChangedEvent(Mathf.Clamp01(progress), center));
        }
    }
}
