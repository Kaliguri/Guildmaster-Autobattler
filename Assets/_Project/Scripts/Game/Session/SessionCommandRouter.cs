using Guildmaster.Guild.Commands;
using UnityEngine;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Единственная дорога к записи в забег для всех, кто пишет: расстановка, награды, магазин,
    /// последствия ивентов. Делегирует шине текущего сеанса, а когда писать некуда — честно отвечает
    /// «не записано» и ничего не делает.
    /// </summary>
    /// <remarks>
    /// <b>Зачем роутер, а не прямая шина.</b> Бой и мероприятие обязаны собираться там, где забега нет
    /// вовсе: дев-арена, Ристалище, PvP-матч, тест. Пока запись резолвилась прямо в скоупе владельца,
    /// такой скоуп просто не поднимался — контейнер падал на отсутствующей регистрации, хотя код
    /// вызывающих давно был готов к ответу «не записано». Теперь тип есть всегда, а сеанс под ним
    /// меняется — в том числе на гостевой, у которого своей шины не будет никогда.
    /// <para><b>«Не записано» — это факт, а не фолбэк.</b> Мы не подменяем поведение и не пишем
    /// «куда-нибудь»: вне забега менять просто нечего, и вызывающие это уже учитывают (см.
    /// <c>DeploymentController</c>, где результат решает, надо ли фиксировать строй).</para>
    /// </remarks>
    public sealed class SessionCommandRouter : IRunCommands
    {
        private readonly SessionHost _sessions;

        public SessionCommandRouter(SessionHost sessions) => _sessions = sessions;

        public bool SetSlotPosition(int slotIndex, Vector2 position)
            => _sessions.Commands?.SetSlotPosition(slotIndex, position) ?? false;

        public bool SetSlotRelic(int slotIndex, string relicId)
            => _sessions.Commands?.SetSlotRelic(slotIndex, relicId) ?? false;

        public bool SetSlotInBattle(int slotIndex, bool inBattle)
            => _sessions.Commands?.SetSlotInBattle(slotIndex, inBattle) ?? false;

        public bool SwapSlots(int a, int b)
            => _sessions.Commands?.SwapSlots(a, b) ?? false;

        public bool SetSlotItem(int slotIndex, int itemSlot, string itemId)
            => _sessions.Commands?.SetSlotItem(slotIndex, itemSlot, itemId) ?? false;

        public void AddGold(int delta) => _sessions.Commands?.AddGold(delta);

        public void RemoveRelic(string relicId) => _sessions.Commands?.RemoveRelic(relicId);

        public void AwardBattleReward() => _sessions.Commands?.AwardBattleReward();

        public void ChooseNode(string nodeId) => _sessions.Commands?.ChooseNode(nodeId);

        public void InflictInjury(int slotIndex, int rollSeed)
            => _sessions.Commands?.InflictInjury(slotIndex, rollSeed);

        public void HealInjury(int slotIndex, string consequenceId, bool payGold)
            => _sessions.Commands?.HealInjury(slotIndex, consequenceId, payGold);

        public bool RequestSave() => _sessions.Commands?.RequestSave() ?? false;
    }
}
