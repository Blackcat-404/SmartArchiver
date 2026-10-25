using System.Text.Json;

namespace SmartArchiver.Archive.Tests;

/// <summary>
/// Пошкоджені архіви дають код помилки, а не виняток. Свої bad_*.arc будуються тут із чесного архіву;
/// справжні файли викладача кладуться в Bad/ разом з expected_failures.json ({"bad_x.arc": "E_HMAC", ...}) і перевіряються теж.
/// </summary>
public class BadArchiveTests
{
    private const string Password = "p@ss";
    private const int NameStart = 47;                // 41 заголовок + 4 file_count + 2 name_len
    private static readonly byte[] Content = TestData.RandomBytes(300, 7);

    private static byte[] Plain() => new ArchiveWriter(1000).Build([TestData.File("a.txt", Content)]);

    private static byte[] Sealed(byte[] plain) => ProtectedArchive.Protect(plain, Password);

    /// <summary>Файл → (пошкоджений архів, очікуваний код). Пошкодження всередині payload запечатуються з вірним HMAC, тож доходять до читача.</summary>
    public static TheoryData<string, ErrorCode> Cases() => new()
    {
        { "bad_magic.arc", ErrorCode.E_MAGIC },
        { "bad_length.arc", ErrorCode.E_LENGTH },
        { "bad_extra_bytes.arc", ErrorCode.E_LENGTH },
        { "bad_hmac.arc", ErrorCode.E_HMAC },
        { "bad_tag.arc", ErrorCode.E_HMAC },
        { "bad_payload.arc", ErrorCode.E_PAYLOAD },
        { "bad_code.arc", ErrorCode.E_CODE },
        { "bad_crc.arc", ErrorCode.E_CRC },
    };

    internal static byte[] Make(string name)
    {
        byte[] ok = Sealed(Plain());
        switch (name)
        {
            case "bad_magic.arc":
                ok[0] ^= 0xFF;
                return ok;
            case "bad_length.arc":
                return ok[..^1];
            case "bad_extra_bytes.arc":
                return [.. ok, 0];
            case "bad_hmac.arc":
                ok[FormatConstants.HeaderSize + 10] ^= 1;
                return ok;
            case "bad_tag.arc":
                ok[^1] ^= 1;
                return ok;
            default:
                byte[] plain = Plain();
                switch (name)
                {
                    case "bad_payload.arc":
                        plain.AsSpan(FormatConstants.HeaderSize, 4).Fill(0xFF);                     // file_count = 4 млрд
                        break;
                    case "bad_code.arc":
                        plain[NameStart + "a.txt".Length + 8 + 4 + 1] = 200;                         // невідома стратегія
                        break;
                    case "bad_crc.arc":
                        plain[^1] ^= 1;                                                              // змінений байт даних
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(name), name, null);
                }
                return Sealed(plain);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DamagedArchive_GivesExpectedCode_AndNoException(string file, ErrorCode expected)
    {
        ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryUnpack([Make(file)], Password);

        Assert.False(result.Ok);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Control_UndamagedArchive_Opens()
    {
        ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryUnpack([Sealed(Plain())], Password);

        Assert.True(result.Ok);
        Assert.Equal(Content, result.Value![0].Data);
    }

    [Fact]
    public void WrongPassword_IsHmacError()
    {
        Assert.Equal(ErrorCode.E_HMAC, ArchivePipeline.TryUnpack([Sealed(Plain())], "інший").Error);
    }

    [Fact]
    public void BadFiles_FromBadFolder_MatchExpectedFailuresJson()
    {
        string json = Path.Combine(AppContext.BaseDirectory, "Bad", "expected_failures.json");
        if (!File.Exists(json))
        {
            return;                                    // файлів викладача ще немає
        }

        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(json))!;
        foreach ((string file, string code) in expected)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Bad", file);
            if (!File.Exists(path))
            {
                continue;
            }
            // Пароль до bad_*.arc: змінна TEST_ARCHIVE_PASSWORD, інакше умовний. Код E_MAGIC/E_LENGTH від пароля не залежить.
            string password = Environment.GetEnvironmentVariable("TEST_ARCHIVE_PASSWORD") ?? Password;
            ArchiveResult<IReadOnlyList<ExtractedFile>> result = ArchivePipeline.TryUnpack([File.ReadAllBytes(path)], password);

            Assert.True(result.Error.HasValue, file);
            Assert.Equal(Enum.Parse<ErrorCode>(code), result.Error);
        }
    }
}
