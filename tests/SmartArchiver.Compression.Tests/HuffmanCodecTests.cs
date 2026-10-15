using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Compression.Tests;

// Тести для алгоритму Гаффмана в SmartArchiver.Core.
// Вони перевіряють цикли кодування/декодування, обробку помилок і перевірку обмежень на довжину коду.
public class HuffmanCodecTests
{
    // Спільний хелпер: закодувати дані, одразу декодувати назад і порівняти
    // з оригіналом. Більшість тестів у цьому файлі — це просто AssertRoundTrip
    // з різними вхідними даними, бо алгоритм Гаффмана поводиться по-різному
    // залежно від розподілу частот байтів.
    private static void AssertRoundTrip(byte[] data)
    {
        byte[] coded = HuffmanCodec.Encode(data);

        Assert.Equal(data, HuffmanCodec.Decode(coded, data.Length));
    }

    // ---------- Завдання 1: round-trip і довжини ----------

    // Найменший можливий непорожній вхід: один байт. Перевіряє, що кодек
    // не ламається на мінімальному розмірі даних.
    [Fact]
    public void RoundTrip_OneByte() => AssertRoundTrip(new byte[] { 42 });

    // Типовий випадок: текст з повторюваними символами (нерівномірний
    // розподіл частот). Це основний сценарій, для якого Гаффман і існує.
    [Fact]
    public void RoundTrip_Text() =>
        AssertRoundTrip(Encoding.UTF8.GetBytes("abracadabra abracadabra"));

    // Усі 256 можливих значень байта по одному разу: рівномірний розподіл
    // частот. Усі довжини кодів виходять однаковими (8 біт), це окремий
    // клас поведінки дерева порівняно з нерівномірним розподілом у тексті.
    [Fact]
    public void RoundTrip_AllByteValues() =>
        AssertRoundTrip(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

    // Великий обсяг випадкових даних з фіксованим seed (тест завжди
    // відтворюваний). Стрес-перевірка на об'ємі, якого немає в інших тестах.
    [Fact]
    public void RoundTrip_Random() =>
        AssertRoundTrip(TestData.RandomBytes(5000, seed: 12345));

    // Методичка 10.24: довжина коду обмежена 15 бітами. Частоти Фібоначчі
    // навмисно дають дерево з глибиною близько 19-20 рівнів — без обмежувача
    // довжини цей тест впав би. Перевіряємо і сам факт обмеження (lengths.Max),
    // і те, що кодек з обмеженими довжинами все одно коректно відновлює дані.
    [Fact]
    public void LengthLimit_TriggersOnFibonacciFrequencies()
    {
        var freq = new long[256];
        long a = 1, b = 1;
        for (int i = 0; i < 20; i++)
        {
            freq[i] = a;
            (a, b) = (b, a + b);
        }

        byte[] lengths = HuffmanLengths.Build(freq);
        Assert.True(lengths.Max() <= CanonicalHuffman.MaxCodeLength);
        CanonicalHuffman.AssignCodes(lengths); // не має кидати

        // Round-trip на даних із такими самими частотами, щоб перевірити
        // не лише довжини, а й реальне кодування/декодування з ними.
        var data = new List<byte>();
        a = 1; b = 1;
        for (int i = 0; i < 20; i++)
        {
            for (long k = 0; k < a; k++) data.Add((byte)i);
            (a, b) = (b, a + b);
        }
        AssertRoundTrip(data.ToArray());
    }

    // Негативний тест (методичка 6.23, 10.64 «truncated»): обрізаємо останні
    // 3 байти закодованого блока. Бітів не вистачає, щоб дочитати останній
    // символ — декодер має впасти з E_PAYLOAD, а не повернути сміття.
    [Fact]
    public void TruncatedStream_ThrowsPayloadError()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello world, hello huffman");
        byte[] cut = HuffmanCodec.Encode(data)[..^3];   // без останніх 3 байтів

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(cut, data.Length));
    }

    // Негативний тест (методичка 6.23 «додати зайвий байт»): після коректного
    // блока дописуємо сторонній байт 0xFF. Декодер прочитає всі rawLength
    // символів успішно, але EnsureOnlyPaddingLeft має помітити, що залишився
    // цілий невикористаний байт, і відхилити блок.
    [Fact]
    public void ExtraTrailingByte_ThrowsPayloadError()
    {
        byte[] data = Encoding.UTF8.GetBytes("hello");
        byte[] extra = HuffmanCodec.Encode(data).Append((byte)0xFF).ToArray();

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(extra, data.Length));
    }

    // Негативний тест (методичка 6.23 «пошкодити Huffman table»): перші три
    // довжини в таблиці рівні 1, що порушує рівність Крафта. Decode має
    // відхилити блок ще до читання бітів (виклик AssignCodes стоїть першим).
    [Fact]
    public void BrokenTable_ThrowsCodeError()
    {
        byte[] coded = new byte[HuffmanCodec.TableSize + 1];
        coded[0] = 1; coded[1] = 1; coded[2] = 1; // Крафт порушено

        TestData.AssertCode(ErrorCode.E_CODE, () => HuffmanCodec.Decode(coded, 1));
    }

    // ---------- Завдання 2: вироджений алфавіт, порожній вхід, відновлення дерева ----------

    // Вироджений алфавіт (методичка 6.5, четверта перевірка): блок з лише
    // одного різного байта, що повторюється. Перевіряє весь цикл кодек-декодек
    // на межовому випадку, а не лише побудову таблиці (як у CanonicalHuffmanTests).
    [Fact]
    public void SingleSymbolRepeated_RoundTrips() =>
        AssertRoundTrip(Enumerable.Repeat((byte)'A', 16).ToArray());

    // Той самий вироджений випадок, але мінімального розміру: один символ
    // один раз. Найменший можливий «виродженний» вхід.
    [Fact]
    public void SingleSymbolOnce_RoundTrips() =>
        AssertRoundTrip(new byte[] { (byte)'Z' });

    // Edge case (методичка 6.21 «empty input»): порожній блок кодувати
    // не можна (порожня таблиця довжин не проходить Крафта). Рішення команди
    // (docs/decisions.md): це помилка виклику програміста (ArgumentException),
    // а не пошкоджені дані архіву (ArchiveException), бо Writer не повинен
    // створювати блоків для порожніх файлів.
    [Fact]
    public void EmptyInput_IsRejectedAtEncode() =>
        Assert.Throws<ArgumentException>(() => HuffmanCodec.Encode(ReadOnlySpan<byte>.Empty));

    // Третя перевірка з методички 6.5 — канонічність: якщо взяти лише
    // таблицю довжин із закодованого потоку (так, як це робитиме реальний
    // декодер) і відновити коди з неї незалежно, вони мають точно збігтися
    // з тим, що записав кодер. Це доводить, що довжин достатньо для повного
    // відновлення кодів — саме це і є сенс канонічного Гаффмана.
    [Fact]
    public void RebuiltCodesFromStoredLengths_MatchEncoderCodes()
    {
        byte[] data = Encoding.UTF8.GetBytes("mississippi river");
        byte[] coded = HuffmanCodec.Encode(data);

        byte[] lengths = coded[..HuffmanCodec.TableSize];
        uint[] rebuilt = CanonicalHuffman.AssignCodes(lengths);

        using var ms = new MemoryStream();
        var bits = new BitWriter(ms);
        foreach (byte b in data)
            bits.WriteBits(rebuilt[b], lengths[b]);
        bits.Flush();

        Assert.Equal(coded[HuffmanCodec.TableSize..], ms.ToArray());
    }
}