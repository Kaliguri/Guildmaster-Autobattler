using System;
using Guildmaster.Net;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Net
{
    /// <summary>
    /// Конверт сообщения: первый байт объявляет канал, остальное едет как есть.
    /// </summary>
    /// <remarks>
    /// <b>Главное здесь — первый тест.</b> Распознавание канала когда-то было рукописным списком рядом
    /// с перечислением, и новый канал в него не дописали: сообщения по нему уходили, а на той стороне
    /// молча отбрасывались. Симптом — «у гостя не появились юниты», причина — за два файла оттуда.
    /// Тест держит правило «каналы объявлены ровно в одном месте» на все будущие каналы сразу.
    /// </remarks>
    public sealed class NetEnvelopeTests
    {
        [Test]
        public void EveryDeclaredChannel_SurvivesTheRoundTrip()
        {
            byte[] buffer = null;
            var body = new byte[] { 7, 8, 9 };

            foreach (NetChannel declared in (NetChannel[])Enum.GetValues(typeof(NetChannel)))
            {
                ArraySegment<byte> wrapped =
                    NetEnvelope.Wrap(declared, new ArraySegment<byte>(body), ref buffer);

                Assert.IsTrue(NetEnvelope.TryUnwrap(wrapped, out NetChannel channel, out ArraySegment<byte> payload),
                    $"Канал {declared} объявлен, а конверт его не узнаёт — сообщения по нему пропадут молча");
                Assert.AreEqual(declared, channel);
                Assert.AreEqual(body.Length, payload.Count, "И нагрузка доехала целиком");
            }
        }

        // Чужой канал — расхождение версий сборки. Отказ здесь громкий по замыслу: молча съеденный
        // пакет ищется по отсутствию картинки, а не по причине.
        [Test]
        public void UnknownChannel_IsRefused()
        {
            var message = new ArraySegment<byte>(new byte[] { 200, 1, 2 });

            Assert.IsFalse(NetEnvelope.TryUnwrap(message, out _, out _));
        }

        [Test]
        public void EmptyMessage_IsRefused()
        {
            Assert.IsFalse(NetEnvelope.TryUnwrap(new ArraySegment<byte>(Array.Empty<byte>()), out _, out _));
        }
    }
}
