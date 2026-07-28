using System;
using UnityEngine;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Что отряд может сделать на привале. Порядок = порядок кнопок на экране; <see cref="MoveOn"/> всегда
    /// последний и всегда бесплатен — уйти можно с любым остатком бюджета.
    /// </summary>
    public enum CampAction
    {
        Empower,       // усилиться
        CopyRelic,     // получить копию реликвии
        Cleanse,       // снять негативное последствие
        HireVessel,    // найм сосуда (или замена старого)
        MoveOn,        // пройти мимо — закрывает привал, стоит 0
    }

    /// <summary>
    /// Состояние одного привала: бюджет действий отряда и его траты. Источник истины — здесь, у флоу; экран
    /// только читает <see cref="Remaining"/> и зовёт <see cref="TryPerform"/>. Так сделано намеренно: копия
    /// счётчика в вью рано или поздно разъезжается с доменом, и вылезает это уже в play-QA.
    /// <para><b>Заглушка по существу:</b> действия пока ничего не делают, кроме списания бюджета — эффекты
    /// (усиление, копия реликвии, снятие последствия, найм) придут вместе с самими механиками. Каркас нужен
    /// раньше, чтобы узел уже стоял на карте и читался в ритме акта.</para>
    /// </summary>
    public sealed class CampSession
    {
        /// <summary>Сколько действий отряд привозит на привал.</summary>
        public const int DefaultBudget = 8;

        /// <summary>Во сколько действий обходится одна трата. Пока одна цена на все — балансится позже.</summary>
        public const int DefaultActionCost = 2;

        public CampSession(int budget = DefaultBudget, int actionCost = DefaultActionCost)
        {
            Budget     = Mathf.Max(0, budget);
            ActionCost = Mathf.Max(1, actionCost);
        }

        /// <summary>Стартовый бюджет действий.</summary>
        public int Budget { get; }

        /// <summary>Цена одной траты в действиях.</summary>
        public int ActionCost { get; }

        /// <summary>Сколько действий уже потрачено.</summary>
        public int Spent { get; private set; }

        /// <summary>Остаток бюджета.</summary>
        public int Remaining => Budget - Spent;

        /// <summary>Хватает ли на ещё одну трату.</summary>
        public bool CanAfford => Remaining >= ActionCost;

        /// <summary>Привал завершён — отряд ушёл (сам, или потому что бюджет кончился и делать больше нечего).</summary>
        public bool IsClosed { get; private set; }

        /// <summary>Меняется на каждую трату и на закрытие — экран перерисовывает по нему кнопки и счётчик.</summary>
        public event Action Changed;

        /// <summary>
        /// Выполнить действие. <see cref="CampAction.MoveOn"/> закрывает привал и всегда проходит; остальные
        /// требуют <see cref="CanAfford"/>. Возвращает <c>false</c>, если действие не прошло (не хватило
        /// бюджета или привал уже закрыт) — экран по этому ответу даёт отказ-фидбэк, а не гасит себя.
        /// </summary>
        public bool TryPerform(CampAction action)
        {
            if (IsClosed) return false;

            if (action == CampAction.MoveOn)
            {
                IsClosed = true;
                Changed?.Invoke();
                return true;
            }

            if (!CanAfford) return false;

            Spent += ActionCost;
            Debug.Log($"[CampSession] - действие '{action}' (-{ActionCost}), остаток {Remaining}/{Budget}. " +
                      "Эффект пока не реализован (каркас привала).");
            Changed?.Invoke();
            return true;
        }
    }
}
