namespace SmartArchiver.Compression;

/// <summary>
/// Запис бітів у побудований потік. Підхід «старший біт першим» робить кодер і декодер
/// синхронними: кожен блок на вході розкладається на послідовність бітів, а наприкінці
/// залишкові біти доповнюються нулями до межі байта.
/// ASSUMPTION: старший біт першим, останній байт доповнюється нулями.
/// Звірити з FORMAT_SPEC_P2.md, коли він з'явиться.
/// Автор: Артем.
/// </summary>
internal sealed class BitWriter
{
    private readonly MemoryStream _output;
    private int _current;
    private int _filled;

    /// <summary>
    /// Створює бітовий потік, який пише байти в вказаний буфер.
    /// </summary>
    public BitWriter(MemoryStream output) => _output = output;

    /// <summary>
    /// Записує <paramref name="length"/> бітів числа <paramref name="value"/>.
    /// Значення передається у порядку старший -> молодший біт, щоб зберегти один формат
    /// для всіх блоків у архіві.
    /// </summary>
    public void WriteBits(uint value, int length)
    {
        for (int k = length - 1; k >= 0; k--)
        {
            _current = (_current << 1) | (int)((value >> k) & 1);
            if (++_filled == 8)
            {
                _output.WriteByte((byte)_current);
                _current = 0;
                _filled = 0;
            }
        }
    }

    /// <summary>
    /// Додає нульові біти в кінці потоку, якщо в поточному байті залишилися вільні позиції.
    /// </summary>
    public void Flush()
    {
        if (_filled == 0)
            return;

        _output.WriteByte((byte)(_current << (8 - _filled)));
        _current = 0;
        _filled = 0;
    }
}

/// <summary>
/// Читання бітів з ReadOnlySpan, починаючи з позиції start. Вихід за межі дає E_PAYLOAD.
/// Після завершення потоку необхідно перевірити, що в залишку немає «зайвих» бітів.
/// Автор: Артем.
/// </summary>
internal ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _next;
    private int _current;
    private int _left;

    /// <summary>
    /// Ініціалізує фрагмент читання, починаючи з байта <paramref name="start"/>.
    /// </summary>
    public BitReader(ReadOnlySpan<byte> data, int start)
    {
        _data = data;
        _next = start;
    }

    /// <summary>
    /// Повертає наступний біт у порядку старший-біт-спочатку.
    /// Якщо в потоці немає байтів, видається помилка E_PAYLOAD.
    /// </summary>
    public int ReadBit()
    {
        if (_left == 0)
        {
            if (_next >= _data.Length)
                throw new ArchiveException(ErrorCode.E_PAYLOAD, "Потік коду обірвано.");

            _current = _data[_next++];
            _left = 8;
        }

        _left--;
        return (_current >> _left) & 1;
    }

    /// <summary>
    /// Зчитує наступний біт; після фізичного кінця повертає віртуальний нуль.
    /// Лише для експериментального арифметичного декодера.
    /// Звичайний ReadBit() і надалі повідомляє про обрізаний архівний payload.
    /// </summary>
    public int ReadBitOrZero() =>
        _left == 0 && _next >= _data.Length ? 0 : ReadBit();

    public uint ReadBits(int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
            value = (value << 1) | (uint)ReadBit();
        return value;
    }
    /// <summary>
    /// Перевіряє, що після даних залишилися лише нульові біти доповнення і жодного зайвого байта.
    /// </summary>
    public readonly void EnsureOnlyPaddingLeft()
    {
        if (_next != _data.Length)
            throw new ArchiveException(ErrorCode.E_PAYLOAD, "Зайві байти після потоку.");

        int padMask = _left == 0 ? 0 : (1 << _left) - 1;
        if ((_current & padMask) != 0)
            throw new ArchiveException(ErrorCode.E_CODE, "Ненульове доповнення.");
    }
}