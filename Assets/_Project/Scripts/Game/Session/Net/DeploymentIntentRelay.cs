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
    /// Намерение гостя, отправленное владельцу арены: «поставь этого бойца сюда».
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
    /// </remarks>
    public sealed class DeploymentIntentSender : IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly ISubscriber<UnitMoveIntent> _moveSub;

        private readonly NetByteWriter _writer = new NetByteWriter(32);
        private byte[] _envelope;
        private IDisposable _subscription;

        public DeploymentIntentSender(INetTransport transport, ISubscriber<UnitMoveIntent> moveSub)
        {
            _transport = transport;
            _moveSub   = moveSub;
        }

        public void Start() => _subscription = _moveSub?.Subscribe(Send);

        public void Dispose() => _subscription?.Dispose();

        private void Send(UnitMoveIntent intent)
        {
            if (!_transport.IsRunning) return;

            _writer.Reset();
            DeploymentIntentCodec.Write(_writer, intent);
            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.DeploymentIntent, _writer.WrittenSegment, ref _envelope),
                NetDelivery.Reliable);
        }
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

        public DeploymentIntentIntake(INetTransport transport, IPublisher<UnitMoveIntent> movePub)
        {
            _transport = transport;
            _movePub   = movePub;
        }

        public void Start() => _transport.MessageReceived += HandleMessage;

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.DeploymentIntent) return;

            _movePub?.Publish(DeploymentIntentCodec.Read(new NetByteReader(payload)));
        }
    }

    /// <summary>
    /// Как намерение выглядит на проводе. Единственный владелец этого формата.
    /// </summary>
    /// <remarks>
    /// Открыт наружу 08.08.2026 не ради вызывающих, а ради прогона туда-обратно: пока кодек был
    /// <c>internal</c>, тест до него не дотягивался, и формат канала держался внимательностью. Соседний
    /// канал с таким же положением дел (состав сеанса) в тот день и разъехался.
    /// </remarks>
    public static class DeploymentIntentCodec
    {
        public static void Write(NetByteWriter writer, in UnitMoveIntent intent)
        {
            writer.WriteInt(intent.UnitId);
            writer.WriteFloat(intent.Position.x);
            writer.WriteFloat(intent.Position.y);
            writer.WriteInt(intent.PlayerId);
        }

        public static UnitMoveIntent Read(NetByteReader reader)
        {
            int   unitId = reader.ReadInt();
            float x      = reader.ReadFloat();
            float y      = reader.ReadFloat();
            int   player = reader.ReadInt();
            return new UnitMoveIntent(unitId, new Vector2(x, y), player);
        }
    }
}
