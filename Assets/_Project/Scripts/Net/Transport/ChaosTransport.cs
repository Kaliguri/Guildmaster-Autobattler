using System;
using System.Collections.Generic;
using Guildmaster.Core.Random;

namespace Guildmaster.Net.Transport
{
    /// <summary>Каким бывает плохой канал. Ноль во всех полях = идеальная сеть.</summary>
    public struct ChaosProfile
    {
        /// <summary>Задержка доставки в шагах <see cref="INetTransport.Poll"/>, минимум.</summary>
        public int MinDelaySteps;

        /// <summary>Задержка, максимум. Разброс между min и max и даёт переупорядочивание.</summary>
        public int MaxDelaySteps;

        /// <summary>Доля потерянных НЕнадёжных сообщений [0..1]. Надёжные не теряются — они надёжные.</summary>
        public float UnreliableLossChance;

        /// <summary>Доля сообщений, доставленных дважды [0..1] — как на стыке реконнекта.</summary>
        public float DuplicateChance;

        /// <summary>Профиль «интернет как он есть»: ~60 мс дороги при 30 шагах в секунду, лёгкие потери.</summary>
        public static ChaosProfile Typical => new ChaosProfile
        {
            MinDelaySteps        = 1,
            MaxDelaySteps        = 3,
            UnreliableLossChance = 0.03f,
            DuplicateChance      = 0.01f,
        };
    }

    /// <summary>
    /// Обёртка над транспортом, портящая канал ПО СИДУ: задержка, разброс задержки (он же
    /// переупорядочивание), потеря ненадёжного, дубли.
    /// <para><b>Зачем свой, если есть Network Simulator.</b> Тот живёт в Multiplayer Tools и работает
    /// только с UTP, а наш релизный транспорт — Facepunch/Steam. Плюс он не даёт того, что здесь главное:
    /// **воспроизводимости**. Из сида падение повторяется вызовом теста, а не ожиданием, пока сеть
    /// «поведёт себя так же». В распределённых системах этот приём называется Deterministic Simulation
    /// Testing, и половина метода у нас уже стоит — детерминированное боевое ядро.</para>
    /// </summary>
    /// <remarks>
    /// Порча живёт на стороне ОТПРАВИТЕЛЯ: сообщение задерживается здесь и уходит во внутренний
    /// транспорт, когда придёт его срок. Так обёртка не зависит от того, чем является inner — loopback,
    /// UTP или Steam.
    /// <para>Время измеряется шагами <see cref="Poll"/>, а не секундами: часы сделали бы тест
    /// недетерминированным ровно в том месте, ради которого он написан.</para>
    /// </remarks>
    public sealed class ChaosTransport : INetTransport
    {
        private readonly struct Pending
        {
            public readonly int          Peer;        // NoPeer = широковещание
            public readonly byte[]       Payload;
            public readonly NetDelivery  Delivery;
            public readonly long         DueStep;

            public Pending(int peer, byte[] payload, NetDelivery delivery, long dueStep)
            {
                Peer     = peer;
                Payload  = payload;
                Delivery = delivery;
                DueStep  = dueStep;
            }
        }

        private readonly INetTransport _inner;
        private readonly ChaosProfile  _profile;
        private readonly XorShiftRng   _rng;
        private readonly List<Pending> _pending = new List<Pending>(32);

        private long _step;

        public ChaosTransport(INetTransport inner, ChaosProfile profile, ulong seed)
        {
            _inner   = inner;
            _profile = profile;
            _rng     = new XorShiftRng(seed);
        }

        public bool IsRunning              => _inner.IsRunning;
        public int  LocalPeerId            => _inner.LocalPeerId;
        public bool IsHost                 => _inner.IsHost;
        public int  MaxReliableMessageBytes => _inner.MaxReliableMessageBytes;

        public event Action<int> PeerConnected
        {
            add    => _inner.PeerConnected += value;
            remove => _inner.PeerConnected -= value;
        }

        public event Action<int> PeerDisconnected
        {
            add    => _inner.PeerDisconnected += value;
            remove => _inner.PeerDisconnected -= value;
        }

        public event Action<int, ArraySegment<byte>> MessageReceived
        {
            add    => _inner.MessageReceived += value;
            remove => _inner.MessageReceived -= value;
        }

        /// <summary>Сколько сообщений сейчас в пути. Для диагностики теста, не для логики.</summary>
        public int InFlight => _pending.Count;

        public void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery) =>
            Hold(peerId, payload, delivery);

        public void SendToAll(ArraySegment<byte> payload, NetDelivery delivery) =>
            Hold(NetPeer.NoPeer, payload, delivery);

        public void Poll()
        {
            _step++;

            // Отпускаем всё, чей срок пришёл. Обход с конца — чтобы удаление не сдвигало непройденные
            // элементы; порядок отправки внутри одного шага при этом обратный порядку постановки, и это
            // не изъян, а часть шума: сеть не обещает, что два сообщения одного кадра придут по порядку.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (p.DueStep > _step) continue;

                _pending.RemoveAt(i);

                if (p.Peer == NetPeer.NoPeer) _inner.SendToAll(new ArraySegment<byte>(p.Payload), p.Delivery);
                else                          _inner.Send(p.Peer, new ArraySegment<byte>(p.Payload), p.Delivery);
            }

            _inner.Poll();
        }

        public void Shutdown()
        {
            _pending.Clear();
            _inner.Shutdown();
        }

        private void Hold(int peer, ArraySegment<byte> payload, NetDelivery delivery)
        {
            // Потеря — только у ненадёжного канала. Терять надёжное значило бы моделировать не сеть, а
            // сломанный транспорт: доставку надёжного обеспечивает он сам, и наш код на неё вправе
            // рассчитывать.
            if (delivery == NetDelivery.Unreliable && Chance(_profile.UnreliableLossChance)) return;

            var bytes = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, bytes, 0, payload.Count);

            _pending.Add(new Pending(peer, bytes, delivery, _step + Delay()));

            // Дубль — отдельная посылка со своей задержкой: на стыке реконнекта копия приходит НЕ
            // вплотную к оригиналу, и идемпотентность обязана держать именно такой случай.
            if (Chance(_profile.DuplicateChance))
                _pending.Add(new Pending(peer, bytes, delivery, _step + Delay()));
        }

        private int Delay()
        {
            int min = _profile.MinDelaySteps > 0 ? _profile.MinDelaySteps : 0;
            int max = _profile.MaxDelaySteps > min ? _profile.MaxDelaySteps : min;
            return min == max ? min : _rng.NextInt(min, max + 1);
        }

        private bool Chance(float probability) =>
            probability > 0f && _rng.NextFloat() < probability;
    }
}
