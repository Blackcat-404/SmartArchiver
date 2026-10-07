namespace SmartArchiver.Core;

/// <summary>Коди помилок з методички (розділ 5 і набір bad_*.arc). Імена збігаються з expected_failures.json.</summary>
public enum ErrorCode
{
    /// <summary>Невірні magic або версія.</summary>
    E_MAGIC,
    /// <summary>Архів обрізаний або має зайві байти, довжини не сходяться.</summary>
    E_LENGTH,
    /// <summary>Не збігся HMAC (з'явиться разом із шифруванням).</summary>
    E_HMAC,
    /// <summary>Структурно некоректний payload, у тому числі небезпечні імена.</summary>
    E_PAYLOAD,
    /// <summary>Невідомий або непідтримуваний код стратегії/стиснення.</summary>
    E_CODE,
    /// <summary>CRC32 файлу не збігся.</summary>
    E_CRC,
}
