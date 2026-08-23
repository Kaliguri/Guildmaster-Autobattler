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
        CopyRelic,     // получить копию мементо
        Cleanse,       // снять негативное последствие
        HireVessel,    // найм сосуда (или замена старого)
        MoveOn,        // пройти мимо — закрывает привал, стоит 0
    }

    /// <summary>
    /// Состояние одного привала: бюджет действий отряда и его траты. Источник истины — здесь, у флоу; экран
    /// только читает <see cref="Remaining"/> и зовёт <see cref="TryPerform"/>. Так сделано намеренно: копия
    /// счётчика в вью рано или поздно разъезжается с доменом, и вылезает это уже в play-QA.
    /// <para><b>Эффект действия сессия не исполняет сама</b>, а зовёт переданный ей исполнитель: снятие
    /// раны — запись в забег, и идти она обязана командой, о которой домен привала знать не должен.
    /// Отказ исполнителя бюджет НЕ тратит — иначе игрок платил бы действием за несостоявшееся лечение.
    /// Исполнителя нет (или он не знает этого действия) — трата проходит как прежде, вхолостую: часть
    /// действий привала своих механик ещё не имеет.</para>
    /// </summary>
    public sealed class CampSession
    {
        /// <summary>Сколько действий отряд привозит на привал.</summary>
        public const int DefaultBudget = 8;

        /// <summary>Во сколько действий обходится одна трата. Пока одна цена на все — балансится позже.</summary>
        public const int DefaultActionCost = 2;

        /// <param name="effect">
        /// Чем исполняется действие: <c>(действие, слот, id последствия) → удалось ли</c>. Возврат
        /// <c>false</c> отменяет трату целиком. <c>null</c> = действия без эффектов (каркас, тесты).
        /// </param>
        public CampSession(int budget = DefaultBudget, int actionCost = DefaultActionCost,
                           Func<CampAction, int, string, bool> effect = null)
        {
            Budget     = Mathf.Max(0, budget);
            ActionCost = Mathf.Max(1, actionCost);
            _effect    = effect;
        }

        private readonly Func<CampAction, int, string, bool> _effect;

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
        /// <param name="slotIndex">Кому — индекс «Сосуда» в гильдии. Нужен действиям, у которых есть цель.</param>
        /// <param name="consequenceId">Что снимаем (<see cref="CampAction.Cleanse"/>).</param>
        public bool TryPerform(CampAction action, int slotIndex = -1, string consequenceId = null)
        {
            if (IsClosed) return false;

            if (action == CampAction.MoveOn)
            {
                IsClosed = true;
                Changed?.Invoke();
                return true;
            }

            if (!CanAfford) return false;

            // Эффект ПЕРВЫМ, бюджет вторым: отказ исполнителя (нечего снимать, промах по цели) не должен
            // стоить игроку действия. Порядок наоборот дал бы «заплатил и не получил».
            if (_effect != null && !_effect(action, slotIndex, consequenceId)) return false;

            Spent += ActionCost;
            Changed?.Invoke();
            return true;
        }
    }
}
