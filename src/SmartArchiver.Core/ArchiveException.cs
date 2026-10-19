namespace SmartArchiver.Core;

/// <summary>Контрольована помилка роботи з архівом. Несе код для порівняння з expected_failures.json.</summary>
public sealed class ArchiveException : Exception
{
    public ErrorCode Code { get; }

    public ArchiveException(ErrorCode code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }
}
