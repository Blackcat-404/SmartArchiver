using System;
using System.Collections.Generic;
using System.Linq;
using SmartArchiver.Core;

namespace SmartArchiver.Compression.Tests;

public class CanonicalHuffmanTests
{
    // Еталонний приклад з підручника: довжини {3,3,3,3,3,2,4,4} мають дати
    // конкретні, заздалегідь відомі коди. Якщо порушено порядок присвоєння
    // кодів (зсув, сортування за довжиною/символом), саме цей тест це покаже.
    [Fact]
    public void ReferenceVector_ProducesExpectedCodes()
    {
        byte[] lengths = { 3, 3, 3, 3, 3, 2, 4, 4 };
        uint[] expected = { 0b010, 0b011, 0b100, 0b101, 0b110, 0b00, 0b1110, 0b1111 };

        Assert.Equal(expected, CanonicalHuffman.AssignCodes(lengths));
    }

    // Вироджений алфавіт (методичка 6.5, 10.26): коли символ лише один,
    // звичайна рівність Крафта не виконується (сума дала б 1/2, а не 1).
    // Формат вимагає окреме правило: довжина = 1, код = 0.
    [Fact]
    public void SingleSymbolWithLengthOne_GetsCodeZero()
    {
        byte[] lengths = { 0, 1, 0 };

        Assert.Equal(0u, CanonicalHuffman.AssignCodes(lengths)[1]);
    }

    // Негативний тест: таблиця, де всі довжини 0, не описує жодного коду.
    // Парсер має відхилити її з E_CODE, а не повернути порожній масив мовчки.
    [Fact]
    public void EmptyTable_Throws()
    {
        TestData.AssertCode(ErrorCode.E_CODE, () => CanonicalHuffman.AssignCodes(new byte[4]));
    }

    // Якщо символ один, довжина обов'язково має бути 1 (див. тест вище).
    // Тут перевіряємо зворотне: довжина 2 для єдиного символу — помилка.
    [Fact]
    public void SingleSymbolWithLengthNotOne_Throws()
    {
        TestData.AssertCode(ErrorCode.E_CODE, () => CanonicalHuffman.AssignCodes(new byte[] { 2 }));
    }

    // Межа формату (методичка 10.24): код не може бути довшим за 15 біт.
    // Довжина 16 має відхилятися ще до перевірки Крафта.
    [Fact]
    public void LengthAboveFifteen_Throws()
    {
        TestData.AssertCode(ErrorCode.E_CODE, () => CanonicalHuffman.AssignCodes(new byte[] { 16, 16 }));
    }

    // Рівність Крафта (методичка 10.25): три символи довжини 1 дають суму
    // 1.5 замість 1 — такий код не може бути префіксним, набір некоректний.
    [Fact]
    public void BrokenKraft_Throws()
    {
        TestData.AssertCode(ErrorCode.E_CODE, () => CanonicalHuffman.AssignCodes(new byte[] { 1, 1, 1 }));
    }
}