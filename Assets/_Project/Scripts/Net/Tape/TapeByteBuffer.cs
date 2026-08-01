using System;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Минимальный писатель байтов с растущим буфером, который переиспользуется между чанками.
    /// <para><b>Почему свой, а не <c>FastBufferWriter</c> из NGO:</b> кодек обязан проверяться в EditMode
    /// без сети и без NGO вовсе — так же, как проверяется лог команд. На отправку готовый массив уходит
    /// одним куском (<c>WrittenSegment</c>), поэтому переезд на <c>FastBufferWriter</c> в момент
    /// подключения named messages не тронет ни формат, ни тесты.</para>
    /// </summary>
    public sealed class TapeByteWriter
    {
        private byte[] _buffer;
        private int    _length;

        public TapeByteWriter(int capacity = 4096) => _buffer = new byte[capacity < 64 ? 64 : capacity];

        /// <summary>Сколько байт записано.</summary>
        public int Length => _length;

        /// <summary>Записанное — без копии. Живёт до следующего <see cref="Reset"/>.</summary>
        public ArraySegment<byte> WrittenSegment => new ArraySegment<byte>(_buffer, 0, _length);

        /// <summary>Начать писать заново, сохранив выделенный буфер: чанки идут потоком, мусорить нельзя.</summary>
        public void Reset() => _length = 0;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)(value & 0xFF);
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteShort(short value) => WriteUShort(unchecked((ushort)value));

        public void WriteUInt(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)(value & 0xFF);
            _buffer[_length++] = (byte)((value >> 8) & 0xFF);
            _buffer[_length++] = (byte)((value >> 16) & 0xFF);
            _buffer[_length++] = (byte)((value >> 24) & 0xFF);
        }

        public void WriteInt(int value) => WriteUInt(unchecked((uint)value));

        public void WriteFloat(float value) => WriteUInt(unchecked((uint)BitConverter.SingleToInt32Bits(value)));

        /// <summary>
        /// Строка как длина + UTF-8. Пустая и <c>null</c> пишутся одинаково — нулевой длиной: у
        /// контентного id разницы между «нет» и «пусто» не бывает.
        /// </summary>
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUShort(0);
                return;
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteUShort((ushort)bytes.Length);
            Ensure(bytes.Length);
            Array.Copy(bytes, 0, _buffer, _length, bytes.Length);
            _length += bytes.Length;
        }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;

            int size = _buffer.Length * 2;
            while (size < _length + extra) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }

    /// <summary>Читатель того же формата. Порядок чтения обязан повторять порядок записи — это и есть контракт.</summary>
    public sealed class TapeByteReader
    {
        private readonly byte[] _buffer;
        private readonly int    _end;
        private int             _position;

        public TapeByteReader(ArraySegment<byte> segment)
        {
            _buffer   = segment.Array;
            _position = segment.Offset;
            _end      = segment.Offset + segment.Count;
        }

        /// <summary>Осталось ли что читать. Ложь на середине структуры — признак битого чанка.</summary>
        public bool HasMore => _position < _end;

        public byte ReadByte()
        {
            if (_position >= _end) throw new InvalidOperationException("чанк кончился раньше формата");
            return _buffer[_position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            byte low  = ReadByte();
            byte high = ReadByte();
            return (ushort)(low | (high << 8));
        }

        public short ReadShort() => unchecked((short)ReadUShort());

        public uint ReadUInt()
        {
            uint b0 = ReadByte();
            uint b1 = ReadByte();
            uint b2 = ReadByte();
            uint b3 = ReadByte();
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        public int ReadInt() => unchecked((int)ReadUInt());

        public float ReadFloat() => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt()));

        public string ReadString()
        {
            int length = ReadUShort();
            if (length == 0) return string.Empty;
            if (_position + length > _end) throw new InvalidOperationException("строка длиннее остатка чанка");

            string value = System.Text.Encoding.UTF8.GetString(_buffer, _position, length);
            _position += length;
            return value;
        }
    }
}
