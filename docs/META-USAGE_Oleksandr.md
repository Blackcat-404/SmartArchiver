# META-USAGE: Захист архівів (SmartArchiver.Core)

**Учасник:** Учасник 3  
**Чекпоінт:** 1  
**Зони:** Виведення ключів, AES-256-CTR, HMAC-SHA256, негативне unit-тестування.

## Використані інструменти

- ChatGPT (OpenAI)

> Нижче наведено приклади формулювань запитів, які відображають теми консультацій із ШІ. Це не дослівний журнал повідомлень. Код — фрагменти підготовленого модуля `ArchiveSecurity.cs` і тестів `ArchiveSecurityTests.cs`. Фактичний запуск тестів у локальному .NET-середовищі ще потрібно перевірити.

---

## 1. Отримання двох ключів за допомогою PBKDF2

**Задача.** Отримати з пароля два незалежні за призначенням 256-бітні ключі: для шифрування та для HMAC.

**Промпт (приклад запиту):**

> Як у C# реалізувати PBKDF2-HMAC-SHA256 із 100 000 ітерацій, 16-байтовою сіллю й результатом 64 байти? Як правильно розділити результат на два ключі по 32 байти?

**Відповідь / застосоване рішення:**

За допомогою `Rfc2898DeriveBytes.Pbkdf2` отримується 64-байтовий матеріал ключів. Перші 32 байти використовуються для AES-256, останні — для HMAC-SHA256. Сіль перевіряється на відповідність заданій довжині; тимчасовий буфер очищується.

```csharp
byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
    password, salt, 100_000, HashAlgorithmName.SHA256, 64);
try
{
    return (derived[..32], derived[32..]);
}
finally
{
    CryptographicOperations.ZeroMemory(derived);
}
```

---

## 2. Шифрування та розшифрування AES-256-CTR

**Задача.** Побудувати CTR-режим поверх стандартного AES, оскільки шифрування та дешифрування мають працювати з однаковою функцією перетворення байтів.

**Промпт (приклад запиту):**

> Як реалізувати AES-256-CTR у .NET, використовуючи AES-ECB лише для генерації блоків ключового потоку? Як поєднати nonce з лічильником і не допустити переповнення?

**Відповідь / застосоване рішення:**

Для кожного 16-байтового блоку створюється лічильник: 12 байтів nonce та 4 байти номера блоку у форматі big-endian. AES шифрує цей лічильник; отриманий блок побітово додається XOR до вхідних даних. Застосування тієї самої операції до шифротексту відновлює початкові байти.

```csharp
for (long block = 0; block < blocks; block++)
{
    BinaryPrimitives.WriteUInt32BigEndian(counter[12..], (uint)block);
    aes.EncryptEcb(counter, streamBlock, PaddingMode.None);

    int offset = checked((int)(block * 16));
    int take = Math.Min(16, input.Length - offset);
    for (int i = 0; i < take; i++)
        result[offset + i] = (byte)(input[offset + i] ^ streamBlock[i]);
}
```

**Уточнення.** Порядок байтів і початкове значення лічильника в наявній реалізації є робочим узгодженням; перед підтримкою еталонних архівів їх слід звірити з `FORMAT_SPEC_P2.md`.

---

## 3. Контроль цілісності через HMAC-SHA256

**Задача.** Виявляти зміну заголовка чи шифротексту та не розшифровувати дані до перевірки автентичності.

**Промпт (приклад запиту):**

> Як у C# обчислювати HMAC-SHA256 для заголовка та шифротексту? Як перевіряти тег без звичайного порівняння масивів, щоб час порівняння не залежав від першого неправильного байта?

**Відповідь / застосоване рішення:**

HMAC обчислюється окремим 32-байтовим ключем. Під час читання архіву спочатку перевіряється тег, а лише потім запускається AES-CTR. Для порівняння застосовується `CryptographicOperations.FixedTimeEquals`.

```csharp
public static byte[] ComputeTag(
    ReadOnlySpan<byte> message, ReadOnlySpan<byte> authenticationKey) =>
    HMACSHA256.HashData(authenticationKey, message);

// Фрагмент перевірки:
byte[] expected = ComputeTag(message, authenticationKey);
try
{
    return CryptographicOperations.FixedTimeEquals(expected, tag);
}
finally
{
    CryptographicOperations.ZeroMemory(expected);
}
```

---

## 4. Захист архіву без зміни решти модулів

**Задача.** Додати шифрування до наявного `SmartArchiver.Core`, не переписуючи `ArchiveWriter`, `ArchiveReader`, базу даних чи графічний інтерфейс Avalonia.

**Промпт (приклад запиту):**

> У проєкті вже працюють ArchiveWriter і ArchiveReader для SARC. Як додати окремий клас захисту, який приймає готовий архів, шифрує payload і передає розшифрований архів назад існуючому читачу?

**Відповідь / застосоване рішення:**

Створено окремий клас `ArchiveSecurity` у `SmartArchiver.Core`. Методи `Protect` і `Unprotect` виконують криптографічні операції, а обгортки `BuildProtected` та `ReadProtected` викликають наявні модулі архівації. Це залишає можливість підключити захист до CLI та Avalonia окремим етапом інтеграції.

```csharp
public static byte[] BuildProtected(
    IReadOnlyList<ArchiveInput> files, string password,
    int blockSize = FormatConstants.DefaultBlockSize) =>
    Protect(new ArchiveWriter(blockSize).Build(files), password);

public static IReadOnlyList<ExtractedFile> ReadProtected(
    byte[] archive, string password) =>
    ArchiveReader.Read(Unprotect(archive, password));
```

**Обмеження:** на CP1 ця обгортка обробляє один том; підключення багатотомного захисту та перевірка відповідності еталонному формату залишаються окремими інтеграційними завданнями.

---

## 5. Негативні тести: пароль, шифротекст і HMAC

**Задача.** Перевірити, що неправильний пароль, підміна даних і пошкодження автентифікаційного тега не призводять до видачі розшифрованого архіву.

**Промпт (приклад запиту):**

> Які xUnit-тести варто додати для PBKDF2, AES-CTR і HMAC? Як перевірити неправильний пароль, змінений nonce, salt, ciphertext та останній байт HMAC?

**Відповідь / застосоване рішення:**

Додано тести на побайтове відновлення файлів після шифрування, симетричність CTR, стабільність виведення ключів, неправильний пароль, зміну автентифікованих даних та обрізаний архів. Очікувані помилки перевіряються через `ArchiveException` і відповідні коди.

```csharp
[Theory]
[InlineData(9)]   // salt
[InlineData(25)]  // nonce
[InlineData(41)]  // ciphertext
[InlineData(-1)]  // HMAC
public void ModifiedAuthenticatedData_RejectsWithHmacError(int index)
{
    byte[] data = ArchiveSecurity.BuildProtected(
        [new ArchiveInput("test", [1, 2, 3])], "secret");
    data[index < 0 ? data.Length - 1 : index] ^= 1;

    Assert.Equal(ErrorCode.E_HMAC,
        Assert.Throws<ArchiveException>(() =>
            ArchiveSecurity.ReadProtected(data, "secret")).Code);
}
```

---

## 6. Перевірка й подальша інтеграція

**Задача.** Передати код команді так, щоб наступний учасник міг підключити захист до інтерфейсу та перевірити сумісність із форматом SARC.

**Промпт (приклад запиту):**

> Як організувати файли криптографічного модуля та його тести у наявній структурі SmartArchiver, щоб вони автоматично потрапляли в .NET-проєкт і не змінювали код інших учасників?

**Відповідь / застосоване рішення:**

Підготовлено окремі файли:

- `src/SmartArchiver.Core/ArchiveSecurity.cs` — криптографічні алгоритми та методи роботи із захищеним архівом.
- `tests/SmartArchiver.Core.Tests/ArchiveSecurityTests.cs` — позитивні й негативні xUnit-тести.
- `docs/PARTICIPANT3_SECURITY.md` — короткий опис модуля для передачі іншому учаснику.

Команди для перевірки на комп'ютері розробника:

```bash
dotnet build
dotnet test
```

**Статус:** файли підготовлено. У межах цієї роботи локальне проходження тестів не підтверджено; інтеграцію в CLI/Avalonia та сумісність з еталонними архівами потрібно перевірити окремо.
