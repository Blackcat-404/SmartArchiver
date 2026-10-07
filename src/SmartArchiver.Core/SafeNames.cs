using System.Text;

namespace SmartArchiver.Core;

/// <summary>
/// Перевірка безпечних імен (методичка, розділ 5): лише ім'я без шляху.
/// Заборонено / \ : NUL, абсолютні шляхи, компоненти "." та "..".
/// Додатково: порожнє ім'я, ім'я лише з крапок, довжина понад 255 байт UTF-8, некоректний Unicode.
/// </summary>
public static class SafeNames
{
    /// <summary>Строгий UTF-8: некоректні послідовності дають виняток замість заміни на U+FFFD.</summary>
    public static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsSafe(string? name, out string reason)
    {
        if (string.IsNullOrEmpty(name))
        {
            reason = "ім'я порожнє";
            return false;
        }

        bool onlyDots = true;
        foreach (char c in name)
        {
            if (c == '/' || c == '\\' || c == ':' || c == '\0')
            {
                reason = "ім'я містить заборонений символ (/ \\ : або NUL)";
                return false;
            }
            if (c != '.')
            {
                onlyDots = false;
            }
        }

        if (onlyDots)
        {
            reason = "ім'я складається лише з крапок (., .. тощо)";
            return false;
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(name);
        }
        catch (EncoderFallbackException)
        {
            reason = "ім'я не є коректним Unicode";
            return false;
        }

        if (byteCount > FormatConstants.MaxNameBytes)
        {
            reason = "ім'я довше за 255 байт у UTF-8";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Кидає ArchiveException(E_PAYLOAD), якщо ім'я небезпечне.</summary>
    public static void EnsureSafe(string? name)
    {
        if (!IsSafe(name, out string reason))
            throw new ArchiveException(ErrorCode.E_PAYLOAD, $"Небезпечне ім'я файлу: {reason}.");
    }
}
