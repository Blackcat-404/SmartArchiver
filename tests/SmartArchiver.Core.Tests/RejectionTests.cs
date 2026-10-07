using System.Text;
using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

/// <summary>Writer і Reader відхиляють небезпечні, пошкоджені й обрізані дані контрольованими помилками.</summary>
public class RejectionTests
{
    // Розкладка архіву з одним файлом "a.txt" на 10 байт (див. FileTableEntry і ArchiveWriter):
    //   0..41   заголовок тому
    //   41..45  file_count
    //   45..47  name_len, 47..52 ім'я "a.txt"
    //   52..60  size, 60..64 crc32, 64 тип, 65 стратегія, 66..74 зміщення
    //   74..89  заголовок блока (15 байт), 89..99 дані
    private const int NameOffset = 47;
    private const int SizeOffset = 52;
    private const int KindOffset = 64;
    private const int StrategyOffset = 65;
    private const int DataOffset = 89;

    private static byte[] SingleFileArchive(string name = "a.txt")
    {
        byte[] data = Encoding.ASCII.GetBytes("0123456789");
        return new ArchiveWriter().Build(new[] { TestData.File(name, data) });
    }

    [Fact]
    public void Layout_AssumptionsOfTheseTests_AreCorrect()
    {
        byte[] archive = SingleFileArchive();
        Assert.Equal(99, archive.Length);
        Assert.Equal((byte)'a', archive[NameOffset]);
        Assert.Equal((byte)'0', archive[DataOffset]);
    }

    // ---------- Writer ----------

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("dir/file")]
    [InlineData("dir\\file")]
    [InlineData("C:x")]
    [InlineData("..")]
    [InlineData("a\0b")]
    public void Writer_RejectsUnsafeNames(string name)
    {
        TestData.AssertCode(ErrorCode.E_PAYLOAD,
            () => new ArchiveWriter().Build(new[] { TestData.File(name, new byte[] { 1 }) }));
    }

    [Fact]
    public void Writer_RejectsDuplicateNames()
    {
        var inputs = new[] { TestData.File("same", new byte[] { 1 }), TestData.File("same", new byte[] { 2 }) };
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => new ArchiveWriter().Build(inputs));
    }

    // ---------- Reader: безпечні імена в архіві ----------

    [Theory]
    [InlineData("..")]
    [InlineData("a/")]
    [InlineData("a\\")]
    [InlineData("a:")]
    [InlineData("\0\0")]
    public void Reader_RejectsUnsafeNamePatchedIntoArchive(string patched)
    {
        byte[] archive = SingleFileArchive("aa");   // ім'я з 2 байтів: підміна не змінює довжин
        byte[] bytes = Encoding.ASCII.GetBytes(patched);
        archive[NameOffset] = bytes[0];
        archive[NameOffset + 1] = bytes[1];

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_RejectsInvalidUtf8Name()
    {
        byte[] archive = SingleFileArchive("aa");
        archive[NameOffset] = 0xFF;
        archive[NameOffset + 1] = 0xFE;

        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    // ---------- Reader: заголовок і довжини ----------

    [Fact]
    public void Reader_BadMagic_E_MAGIC()
    {
        byte[] archive = SingleFileArchive();
        archive[1] ^= 0xFF;
        TestData.AssertCode(ErrorCode.E_MAGIC, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_TrailingBytes_E_LENGTH()
    {
        byte[] archive = SingleFileArchive().Concat(new byte[] { 0 }).ToArray();
        TestData.AssertCode(ErrorCode.E_LENGTH, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_EveryTruncatedPrefix_E_LENGTH()
    {
        byte[] archive = new ArchiveWriter(8).Build(new[]
        {
            TestData.File("a", TestData.RandomBytes(30)),
            TestData.File("b", Array.Empty<byte>()),
        });

        for (int length = 0; length < archive.Length; length++)
        {
            byte[] truncated = archive.AsSpan(0, length).ToArray();
            TestData.AssertCode(ErrorCode.E_LENGTH, () => ArchiveReader.Read(truncated));
        }
    }

    [Fact]
    public void Reader_MultiVolumeHeader_IsRejectedForNow()
    {
        byte[] archive = SingleFileArchive();
        archive[8] = 2;   // volume_count = 2
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    // ---------- Reader: payload ----------

    [Fact]
    public void Reader_ChangedDataByte_E_CRC()
    {
        byte[] archive = SingleFileArchive();
        archive[DataOffset + 3] ^= 0x01;
        TestData.AssertCode(ErrorCode.E_CRC, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_SizeInTableDiffersFromBlocks_E_PAYLOAD()
    {
        byte[] archive = SingleFileArchive();
        archive[SizeOffset + 7] += 1;   // 10 -> 11
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_HugeDeclaredSize_FailsWithoutHugeAllocation()
    {
        byte[] archive = SingleFileArchive();
        archive[SizeOffset] = 0x7F;   // ~9 ексабайтів
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_HugeFileCount_FailsWithoutHugeAllocation()
    {
        byte[] archive = SingleFileArchive();
        archive[41] = 0xFF;   // file_count ≈ 4 мільярди
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_UnsupportedStrategyInTable_E_CODE()
    {
        byte[] archive = SingleFileArchive();
        archive[StrategyOffset] = (byte)CompressionStrategy.Huffman;
        TestData.AssertCode(ErrorCode.E_CODE, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_UnknownStrategyInTable_E_CODE()
    {
        byte[] archive = SingleFileArchive();
        archive[StrategyOffset] = 200;
        TestData.AssertCode(ErrorCode.E_CODE, () => ArchiveReader.Read(archive));
    }

    [Fact]
    public void Reader_UnknownFileKind_E_PAYLOAD()
    {
        byte[] archive = SingleFileArchive();
        archive[KindOffset] = 200;
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => ArchiveReader.Read(archive));
    }

    // ---------- Загальна стійкість ----------

    [Fact]
    public void Reader_AnyChangedByte_NeverThrowsAnythingExceptArchiveException()
    {
        byte[] original = new ArchiveWriter(8).Build(new[]
        {
            TestData.File("a.txt", TestData.RandomBytes(20)),
            TestData.File("b.txt", Array.Empty<byte>()),
            TestData.File("c.txt", new byte[] { 1 }),
        });

        for (int i = 0; i < original.Length; i++)
        {
            foreach (byte mask in new byte[] { 0x01, 0x80, 0xFF })
            {
                byte[] mutated = (byte[])original.Clone();
                mutated[i] ^= mask;
                try
                {
                    ArchiveReader.Read(mutated);   // допустимо: зміна могла потрапити в сіль/nonce
                }
                catch (ArchiveException)
                {
                    // очікувана контрольована відмова
                }
            }
        }
    }

    [Fact]
    public void Reader_RandomGarbage_NeverThrowsAnythingExceptArchiveException()
    {
        var rng = new Random(123);
        for (int i = 0; i < 500; i++)
        {
            byte[] garbage = new byte[rng.Next(0, 200)];
            rng.NextBytes(garbage);
            if (garbage.Length >= FormatConstants.HeaderSize && i % 2 == 0)
            {
                // Коректний заголовок з правильною довжиною, щоб випадковий payload доходив до розбору.
                new VolumeHeader { CiphertextLength = (uint)(garbage.Length - FormatConstants.HeaderSize) }.WriteTo(garbage);
            }

            try
            {
                ArchiveReader.Read(garbage);
            }
            catch (ArchiveException)
            {
            }
        }
    }
}
