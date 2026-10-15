using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Compression.Tests;

/// <summary>
/// Префіксність, канонічність, межі, крайні випадки і негативні тести для Гаффмана
/// (методичка 6.5, 6.21–6.25, 10.63–10.64).
/// </summary>
public class HuffmanExtendedTests
{
    // Спільний хелпер для перевірки префіксності (методичка 6.5, друга
    // перевірка): жоден код не повинен бути початком іншого коду. Якщо є
    // два символи a і b, де код a коротший за код b, перевіряємо, що перші
    // lengths[a] бітів коду b не дорівнюють коду a.
    private static void AssertPrefixFree(byte[] lengths)
    {
        uint[] codes = CanonicalHuffman.AssignCodes(lengths);
        int[] used = Enumerable.Range(0, lengths.Length).Where(s => lengths[s] != 0).ToArray();

        foreach (int a in used)
        {
            foreach (int b in used)
            {
                if (a == b || lengths[a] > lengths[b])
                {
                    continue;
                }

                if (lengths[a] == lengths[b])
                {
                    Assert.NotEqual(codes[a], codes[b]);
                }
                else
                {
                    // Код a коротший: перевіряємо, що він не збігається з початком коду b.
                    Assert.NotEqual(codes[a], codes[b] >> (lengths[b] - lengths[a]));
                }
            }
        }
    }

    // ---------- Префіксність і канонічність ----------

    // Перевірка префіксності на тому самому еталонному векторі, що й
    // у CanonicalHuffmanTests.ReferenceVector. Там перевірялись конкретні
    // значення кодів, тут — структурна властивість (жоден не є префіксом іншого).
    [Fact]
    public void Codes_ArePrefixFree_ForReferenceVector()
    {
        AssertPrefixFree(new byte[] { 3, 3, 3, 3, 3, 2, 4, 4 });
    }

    // Та сама перевірка, але на довжинах, побудованих реальним алгоритмом
    // (HuffmanLengths.Build) з різних текстів, а не на вручну підібраному
    // еталоні. Ловить помилки, які еталонний вектор міг не покрити.
    [Theory]
    [InlineData("abracadabra")]
    [InlineData("mississippi river, достатньо різноманітний текст для різних частот")]
    public void Codes_ArePrefixFree_ForBuiltTrees(string text)
    {
        var freq = new long[HuffmanCodec.TableSize];
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            freq[b]++;
        }

        AssertPrefixFree(HuffmanLengths.Build(freq));
    }

    // Структурна властивість канонічності: серед символів з однаковою
    // довжиною коду код зростає разом із індексом символу (методичка 10.23).
    // Це окрема перевірка від "значення конкретного коду правильне".
    [Fact]
    public void SameLengthCodes_IncreaseWithSymbolIndex()
    {
        byte[] lengths = { 3, 3, 3, 3, 3, 2, 4, 4 };
        uint[] codes = CanonicalHuffman.AssignCodes(lengths);

        for (int a = 0; a < lengths.Length; a++)
        {
            for (int b = a + 1; b < lengths.Length; b++)
            {
                if (lengths[a] == lengths[b])
                {
                    Assert.True(codes[a] < codes[b]);
                }
            }
        }
    }

    // ---------- Межі довжини ----------

    // Boundary test (методичка 6.22): перевіряємо саме межове значення 15
    // (на відміну від CanonicalHuffmanTests.LengthAboveFifteen_Throws, який
    // перевіряє 16 — "за межею"). Набір довжин підібраний так, щоб сума
    // Крафта дорівнювала точно 1: довжини 1..14 по одному символу плюс два
    // символи довжини 15 закривають залишок.
    [Fact]
    public void MaxLengthFifteen_IsAccepted_WhenKraftHolds()
    {
        byte[] lengths = Enumerable.Range(1, 14)
            .Select(i => (byte)i)
            .Concat(new byte[] { 15, 15 })
            .ToArray();

        uint[] codes = CanonicalHuffman.AssignCodes(lengths);

        Assert.Equal(16, codes.Length);
        AssertPrefixFree(lengths);
    }

    // ---------- Крайні випадки кодека ----------

    // Перевіряє не лише round-trip (це вже є в HuffmanCodecTests), а саму
    // таблицю довжин у закодованому блоці: рівно один символ ('A') повинен
    // мати довжину 1, усі інші — 0 (не використовуються).
    [Fact]
    public void OneSymbolBlock_TableHasLengthOneForThatSymbolOnly()
    {
        byte[] coded = HuffmanCodec.Encode(Enumerable.Repeat((byte)'A', 16).ToArray());

        for (int s = 0; s < HuffmanCodec.TableSize; s++)
        {
            Assert.Equal((byte)(s == 'A' ? 1 : 0), coded[s]);
        }
    }

    // Обов'язковий маленький тест з методички (10.63 "all-zero file"):
    // блок із самих нульових байтів. Усі частоти зосереджені на символі 0,
    // тож кожен байт кодується одним бітом. Перевіряємо і точний розмір
    // результату (таблиця 256 байт + 1 біт на вхідний байт), і сам round-trip.
    [Fact]
    public void AllZeroBlock_UsesOneBitPerByte()
    {
        byte[] data = new byte[1000];
        byte[] coded = HuffmanCodec.Encode(data);

        // Таблиця 256 байтів + 1000 біт, тобто 125 байтів.
        Assert.Equal(HuffmanCodec.TableSize + (1000 + 7) / 8, coded.Length);
        Assert.Equal((byte)1, coded[0]);
        Assert.Equal(data, HuffmanCodec.Decode(coded, data.Length));
    }

    // ---------- Негативні тести ----------

    // Edge case: rawLength = 0 не має сенсу (порожній блок не кодується,
    // див. HuffmanCodecTests.EmptyInput_IsRejectedAtEncode) — Decode має
    // явно відхилити цей виклик, а не повернути порожній масив мовчки.
    [Fact]
    public void Decode_RawLengthZero_IsRejected()
    {
        byte[] coded = HuffmanCodec.Encode(new byte[] { 1, 2, 3 });

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(coded, 0));
    }

    // Негативний тест: rawLength із заголовка блока каже декодувати набагато
    // більше символів, ніж фізично є бітів у потоці. Декодер має впасти
    // з контрольованою помилкою, а не намагатися читати за межі масиву.
    [Fact]
    public void Decode_RawLengthLongerThanStream_IsRejected()
    {
        byte[] coded = HuffmanCodec.Encode(new byte[] { 1, 2, 3 });

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(coded, 1000));
    }

    // Межовий випадок: у потоці є лише таблиця довжин (256 байтів), жодного
    // біта закодованих даних, але rawLength вимагає хоча б один символ.
    // Декодер має помітити, що бітів для читання не залишилось.
    [Fact]
    public void Decode_TableOnlyWithPositiveRawLength_IsRejected()
    {
        byte[] table = HuffmanCodec.Encode(new byte[] { 65 })[..HuffmanCodec.TableSize];

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(table, 1));
    }

    // Розширена версія TruncatedStream_ThrowsPayloadError: замість одного
    // конкретного обрізання (останні 3 байти) перевіряємо геть усі можливі
    // довжини обрізаного потоку — від 0 до повної довжини мінус один байт.
    // Ловить ситуації, коли обрив стається в "незручному" місці (посеред
    // таблиці, на межі байта тощо), яке один фіксований тест міг пропустити.
    [Fact]
    public void EveryTruncation_ThrowsPayloadError()
    {
        byte[] data = TestData.RandomBytes(300, seed: 9);
        byte[] coded = HuffmanCodec.Encode(data);

        for (int length = 0; length < coded.Length; length++)
        {
            byte[] cut = coded[..length];
            TestData.AssertCode(ErrorCode.E_PAYLOAD, () => HuffmanCodec.Decode(cut, data.Length));
        }
    }

    // Методичка 6.24 — головна вимога: пошкоджені дані ніколи не повинні
    // викликати необроблений виняток (IndexOutOfRangeException, OverflowException
    // тощо), лише контрольований ArchiveException. Тест інвертує кожен біт
    // у кожному байті закодованого блока (три різні маски) і впевнюється,
    // що будь-яка помилка — саме ArchiveException. Якщо вилетить щось інше,
    // catch його не зловить, і тест провалиться автоматично.
    [Fact]
    public void EveryByteFlip_ThrowsOnlyArchiveException()
    {
        byte[] data = TestData.RandomBytes(200, seed: 3);
        byte[] original = HuffmanCodec.Encode(data);

        foreach (int index in Enumerable.Range(0, original.Length))
        {
            foreach (byte mask in new byte[] { 0x01, 0x80, 0xFF })
            {
                byte[] mutated = (byte[])original.Clone();
                mutated[index] ^= mask;

                try
                {
                    HuffmanCodec.Decode(mutated, data.Length);
                }
                catch (ArchiveException)
                {
                    // Очікувана контрольована відмова.
                }
            }
        }
    }

    // Fuzz test (методичка 6.25): повністю випадкові байти замість
    // валідного архіву. Перевіряє ту саму вимогу 6.24, але на даних, які
    // взагалі ніяк не пов'язані з форматом Гаффмана, а не на мутаціях
    // коректного блока, як у тесті вище.
    [Fact]
    public void RandomGarbage_ThrowsOnlyArchiveException()
    {
        var rng = new Random(77);
        for (int i = 0; i < 300; i++)
        {
            byte[] garbage = new byte[rng.Next(0, 400)];
            rng.NextBytes(garbage);

           try
            {           
                HuffmanCodec.Decode(garbage, rng.Next(1, 200));
            }
            catch (Exception ex)
            {
                Assert.IsType<ArchiveException>(ex);
            }
        }
    }
}