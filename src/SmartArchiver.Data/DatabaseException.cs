namespace SmartArchiver.Data;

/// <summary>
/// The database is misconfigured, unreachable or rejected the data. The message never contains the password.
/// </summary>
public sealed class DatabaseException(string message, Exception? innerException = null)
    : Exception(message, innerException);
