using System;
using Guildmaster.Core.Arena;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Намерения гостя, отправленные владельцу арены: «поставь этого бойца сюда» и «надень на него
    /// эту реликвию».
    /// </summary>
    /// <remarks>
    /// <b>Мост, а не логика.</b> Руки игрока не знают, кто их владелец, и публикуют намерение
    /// одинаково в соло, у хозяина и у гостя; этот класс существует ровно затем, чтобы у гостя оно
    /// доехало до того, кто вправе его исполнить. Собирается только у гостя — у хозяина отправлять
    /// некому и незачем, он исполняет своё намерение сам.
    /// <para><b>Надёжной доставки достаточно, номеров нет.</b> Намерение не идемпотентно по смыслу:
    /// повтор «поставь сюда» даёт тот же результат, а его потеря видна игроку сразу — боец не
    /// переехал, и рука тянется снова. Городить нумерацию, как у команд забега, значило бы платить
    /// за защиту от того, что и так безвредно.</para>
    /// <para><b>Реликвия поехала тем же каналом 08.08.2026.</b> До этого возилось только перемещение,
    /// а <c>EquipRelicRequest</c> у гостя публиковался в шину, где подписчика нет: исполнитель
    /// (<c>DeploymentController</c>) собирается только владельцу. Локальная публикация без адресата
    /// не бросает и не логирует, поэтому жест проходил, звук успеха играл, и не происходило ничего —
    /// ни у кого (наход. Макса в прогоне вдвоём).</para>
    /// </remarks>
    public sealed class DeploymentIntentSender : IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly ISubscriber<UnitMoveIntent> _moveSub;
        private readonly ISubscriber<Guildmaster.Data.Definitions.EquipRelicRequest> _equipSub;

        private readonly NetByteWriter _writer = new NetByteWriter(32);
        private byte[] _envelope;
        private IDisposable _moveSubscription;
        private IDisposable _equipSubscription;

        public DeploymentIntentSender(INetTransport transport, ISubscriber<UnitMoveIntent> moveSub,
                                      ISubscriber<Guildmaster.Data.Definitions.EquipRelicRequest> equipSub)
        {
            _transport = transport;
            _moveSub   = moveSub;
            _equipSub  = equipSub;
        }

        public void Start()
        {
            _moveSubscription  = _moveSub?.Subscribe(Send);
            _equipSubscription = _equipSub?.Subscribe(Send);
        }

        public void Dispose()
        {
            _moveSubscription?.Dispose();
            _equipSubscription?.Dispose();
        }

        private void Send(UnitMoveIntent intent)
        {
            if (!_transport.IsRunning) return;
            SendPacket(DeploymentIntentCodec.WriteMove(_writer, intent));
        }

        /// <summary>
        /// Реликвия едет строковым id, а не объектом: по проводу ездит контент, а не ссылки на ассеты.
        /// Совпадение реестров уже проверено рукопожатием, поэтому владелец соберёт ту же реликвию.
        /// </summary>
        private void Send(Guildmaster.Data.Definitions.EquipRelicRequest request)
        {
            if (!_transport.IsRunning || request.Relic == null) return;
            SendPacket(DeploymentIntentCodec.WriteEquip(_writer, request.UnitId, request.Relic.Id));
        }

        private void SendPacket(ArraySegment<byte> payload) =>
            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.DeploymentIntent, payload, ref _envelope),
                NetDelivery.Reliable);
    }

    /// <summary>
    /// Владельческая половина: принимает намерения гостей и публикует их у себя как свои.
    /// </summary>
    /// <remarks>
    /// Дальше их путь ничем не отличается от пути собственного клика — тем же намерением, к тому же
    /// исполнителю, с той же перепроверкой права и зоны. Разных дорог к одному результату быть не
    /// должно: вторая разъехалась бы с первой ровно там, где это труднее всего заметить.
    /// <para><b>Автора берём ИЗ ПАКЕТА, а не из номера пира</b>, потому что право проверяется по
    /// участнику сеанса, а не по соединению. Подмена тут возможна и сегодня ничем не карается — это
    /// осознанно: кооп у нас доверительный, играют по приглашению из друзей Steam.</para>
    /// </remarks>
    public sealed class DeploymentIntentIntake : IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly IPublisher<UnitMoveIntent> _movePub;
        private readonly IPublisher<Guildmaster.Data.Definitions.EquipRelicRequest> _equipPub;
        private readonly Guildmaster.Data.Definitions.IContentDatabase _content;

        public DeploymentIntentIntake(INetTransport transport, IPublisher<UnitMoveIntent> movePub,
                                      IPublisher<Guildmaster.Data.Definitions.EquipRelicRequest> equipPub,
                                      Guildmaster.Data.Definitions.IContentDatabase content)
        {
            _transport = transport;
            _movePub   = movePub;
            _equipPub  = equipPub;
            _content   = content;
        }

        public void Start() => _transport.MessageReceived += HandleMessage;

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.DeploymentIntent) return;

            if (!DeploymentIntentCodec.TryRead(payload, out DeploymentIntent intent))
            {
                Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Commands,
                    $"хозяин: намерение расстановки от пира {from} не разобралось — формат канала разъехался");
                return;
            }

            if (!intent.IsEquip)
            {
                _movePub?.Publish(intent.Move);
                return;
            }

            // Реликвию собираем из СВОЕГО реестра по id: по проводу ездит контент, а не ссылки на
            // ассеты. Промах здесь означает расхождение реестров, которое рукопожатие обязано было
            // поймать раньше, — молчать о нём нельзя.
            if (_content != null &&
                _content.TryGet(intent.RelicId, out Guildmaster.Data.Definitions.RelicData relic))
            {
                _equipPub?.Publish(new Guildmaster.Data.Definitions.EquipRelicRequest(intent.UnitId, relic));
                return;
            }

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Commands,
                $"хозяин: реликвии «{intent.RelicId}» нет в реестре — надеть её пиру {from} нечем");
        }
    }

    /// <summary>
    /// Разобранное намерение расстановки: либо «поставь бойца сюда», либо «надень на него реликвию».
    /// </summary>
    public readonly struct DeploymentIntent
    {
        public readonly bool           IsEquip;
        public readonly UnitMoveIntent Move;
        public readonly int            UnitId;
        public readonly string         RelicId;

        private DeploymentIntent(bool isEquip, UnitMoveIntent move, int unitId, string relicId)
        {
            IsEquip = isEquip;
            Move    = move;
            UnitId  = unitId;
            RelicId = relicId;
        }

        public static DeploymentIntent OfMove(in UnitMoveIntent move) =>
            new DeploymentIntent(false, move, move.UnitId, null);

        public static DeploymentIntent OfEquip(int unitId, string relicId) =>
            new DeploymentIntent(true, default, unitId, relicId);
    }

    /// <summary>
    /// Как намерение выглядит на проводе. Единственный владелец этого формата.
    /// </summary>
    /// <remarks>
    /// Открыт наружу 08.08.2026 не ради вызывающих, а ради прогона туда-обратно: пока кодек был
    /// <c>internal</c>, тест до него не дотягивался, и формат канала держался внимательностью. Соседний
    /// канал с таким же положением дел (состав сеанса) в тот день и разъехался.
    /// <para><b>Вид намерения объявляет первый байт.</b> Различать их по длине пакета нельзя: так уже
    /// сделали на канале решений, и развилка держалась ровно до первого изменения формата.</para>
    /// </remarks>
    public static class DeploymentIntentCodec
    {
        private const byte MoveTag  = 0;
        private const byte EquipTag = 1;

        public static ArraySegment<byte> WriteMove(NetByteWriter writer, in UnitMoveIntent intent)
        {
            writer.Reset();
            writer.WriteByte(MoveTag);
            writer.WriteInt(intent.UnitId);
            writer.WriteFloat(intent.Position.x);
            writer.WriteFloat(intent.Position.y);
            writer.WriteInt(intent.PlayerId);
            return writer.WrittenSegment;
        }

        public static ArraySegment<byte> WriteEquip(NetByteWriter writer, int unitId, string relicId)
        {
            writer.Reset();
            writer.WriteByte(EquipTag);
            writer.WriteInt(unitId);
            writer.WriteString(relicId);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать намерение. <c>false</c> — пакет не разобрался целиком; исполнять половину нельзя.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out DeploymentIntent intent)
        {
            intent = default;

            var bytes = new NetByteReader(payload);

            try
            {
                byte tag = bytes.ReadByte();

                if (tag == MoveTag)
                {
                    int   unitId = bytes.ReadInt();
                    float x      = bytes.ReadFloat();
                    float y      = bytes.ReadFloat();
                    int   player = bytes.ReadInt();
                    if (bytes.HasMore) return false;

                    intent = DeploymentIntent.OfMove(new UnitMoveIntent(unitId, new Vector2(x, y), player));
                    return true;
                }

                if (tag == EquipTag)
                {
                    int    unitId  = bytes.ReadInt();
                    string relicId = bytes.ReadString();
                    if (bytes.HasMore || string.IsNullOrEmpty(relicId)) return false;

                    intent = DeploymentIntent.OfEquip(unitId, relicId);
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            return false; // вид намерения этой сборке неизвестен
        }
    }
}
