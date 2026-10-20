using System.Text.Json;

namespace SmartArchiver.Archive;

/// <summary>
/// Цінність блока при пакуванні томів: число за типом його файлу (пріоритети_типів з team_config.json).
/// Усі блоки одного типу мають однакову цінність.
/// </summary>
public sealed class PackingPriorities
{
    /// <summary>Кількість типів файлів у FORMAT_SPEC_P2: file_type 0…6.</summary>
    public const int KindCount = 7;

    private readonly long[] _values;

    /// <summary>
    /// Значення за замовчуванням, поки немає справжнього team_config.json: текст і код важливіші за стиснені дані.
    /// Це ЗАГЛУШКА: справжні числа однакові для всіх команд і беруться з team_config.json (<see cref="TeamConfig"/>).
    /// </summary>
    public static PackingPriorities Default { get; } = new([5, 5, 4, 3, 3, 2, 1]);

    public PackingPriorities(IReadOnlyList<long> valuesByKind)
    {
        ArgumentNullException.ThrowIfNull(valuesByKind);
        if (valuesByKind.Count != KindCount)
            throw new ArgumentException($"Потрібно рівно {KindCount} пріоритетів (за типами 0…6).", nameof(valuesByKind));
        foreach (long value in valuesByKind)
        {
            if (value < 0)
                throw new ArgumentException("Пріоритет не може бути від'ємним.", nameof(valuesByKind));
        }
        _values = [.. valuesByKind];
    }

    /// <summary>Пріоритети за типами 0…6.</summary>
    public IReadOnlyList<long> Values => _values;

    /// <summary>Цінність блока файлу цього типу. Для «ще не класифіковано» це найменший пріоритет.</summary>
    public long ValueOf(FileKind kind) =>
        (int)kind < KindCount ? _values[(int)kind] : _values.Min();

    /// <summary>
    /// Читає пріоритети з JSON: масив із 7 чисел або об'єкт з ключами "0"…"6".
    /// </summary>
    public static PackingPriorities FromJson(JsonElement element)
    {
        var values = new long[KindCount];
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                if (element.GetArrayLength() != KindCount)
                    throw new ArgumentException($"Масив пріоритетів має містити {KindCount} чисел.");
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    values[index++] = ReadNumber(item);
                }
                break;
            case JsonValueKind.Object:
                for (int kind = 0; kind < KindCount; kind++)
                {
                    if (!element.TryGetProperty(kind.ToString(System.Globalization.CultureInfo.InvariantCulture), out JsonElement item))
                        throw new ArgumentException($"У пріоритетах немає ключа \"{kind}\".");
                    values[kind] = ReadNumber(item);
                }
                break;
            default:
                throw new ArgumentException("Пріоритети мають бути масивом із 7 чисел або об'єктом з ключами 0…6.");
        }
        return new PackingPriorities(values);
    }

    private static long ReadNumber(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out long value))
            throw new ArgumentException("Пріоритет має бути цілим числом.");
        return value;
    }
}

/// <summary>Параметри пакування з team_config.json: місткість тому й пріоритети типів.</summary>
public sealed record TeamConfig(long MaxVolumeBytes, PackingPriorities Priorities)
{
    /// <summary>
    /// Читає max_volume_bytes і пріоритети_типів. Точну форму файлу команді не надали,
    /// тому пріоритети приймаються і масивом, і об'єктом (див. <see cref="PackingPriorities.FromJson"/>).
    /// </summary>
    public static TeamConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("max_volume_bytes", out JsonElement max)
                || max.ValueKind != JsonValueKind.Number
                || !max.TryGetInt64(out long maxBytes))
            {
                throw new ArgumentException("У team_config.json немає цілого max_volume_bytes.");
            }
            if (!root.TryGetProperty("пріоритети_типів", out JsonElement priorities))
                throw new ArgumentException("У team_config.json немає пріоритети_типів.");

            return new TeamConfig(maxBytes, PackingPriorities.FromJson(priorities));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"team_config.json не є коректним JSON: {ex.Message}", ex);
        }
    }

    public static TeamConfig Load(string path) => Parse(File.ReadAllText(path));
}
