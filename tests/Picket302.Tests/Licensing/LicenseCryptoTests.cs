using System.Security.Cryptography;
using System.Text;
using Picket302.Infrastructure.Licensing;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// Проверка шифрования данных лицензии (ТЗ §8.10, функция 124:
/// «Редактирование зашифрованного блока вручную переводит приложение в trial,
/// а не в падение»).
/// </summary>
public sealed class LicenseCryptoTests
{
    [Fact]
    public void ProtectИUnprotect_ВозвращаютИсходныеДанные()
    {
        var шифровальщик = new LicenseCrypto("A1B2C3D4-E5F6");
        var данные = Encoding.UTF8.GetBytes("{\"edition\":\"Home\",\"owner\":\"ООО Проверка\"}");

        var защищённый = шифровальщик.Protect(данные);

        Assert.NotEqual(данные, защищённый);

        var открытые = шифровальщик.Unprotect(защищённый);

        Assert.Equal(данные, открытые);
    }

    [Fact]
    public void Protect_ФорматБлока_СоответствуетОписанию()
    {
        var шифровальщик = new LicenseCrypto("машина");
        var данные = new byte[] { 1, 2, 3 };

        var защищённый = шифровальщик.Protect(данные);

        Assert.Equal(LicenseCrypto.РазмерПрефикса + данные.Length, защищённый.Length);
        Assert.Equal(LicenseCrypto.ВерсияФормата, защищённый[0]);
    }

    [Fact]
    public void Protect_Дважды_ДаётРазныеБлоки()
    {
        var шифровальщик = new LicenseCrypto("машина");
        var данные = Encoding.UTF8.GetBytes("данные лицензии");

        var первый = шифровальщик.Protect(данные);
        var второй = шифровальщик.Protect(данные);

        Assert.NotEqual(первый, второй);
        Assert.Equal(данные, шифровальщик.Unprotect(второй));
    }

    [Fact]
    public void Unprotect_ПовреждённыйШифротекст_ВозвращаетОшибку()
    {
        var шифровальщик = new LicenseCrypto("машина");
        var защищённый = шифровальщик.Protect(Encoding.UTF8.GetBytes("данные лицензии"));

        защищённый[защищённый.Length - 1] ^= 0xFF;

        var ошибка = Assert.Throws<CryptographicException>(() => шифровальщик.Unprotect(защищённый));

        Assert.Contains("расшифровать", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unprotect_ИспорченнаяМетка_ВозвращаетОшибку()
    {
        var шифровальщик = new LicenseCrypto("машина");
        var защищённый = шифровальщик.Protect(Encoding.UTF8.GetBytes("данные лицензии"));

        защищённый[1 + LicenseCrypto.РазмерСоли + LicenseCrypto.РазмерNonce] ^= 0x5A;

        Assert.Throws<CryptographicException>(() => шифровальщик.Unprotect(защищённый));
    }

    [Fact]
    public void Unprotect_КлючДругойМашины_ВозвращаетОшибку()
    {
        var первый = new LicenseCrypto("машина-1");
        var второй = new LicenseCrypto("машина-2");

        var защищённый = первый.Protect(Encoding.UTF8.GetBytes("данные лицензии"));

        Assert.Throws<CryptographicException>(() => второй.Unprotect(защищённый));
    }

    [Fact]
    public void Unprotect_ОбрезанныйБлок_ВозвращаетОшибку()
    {
        var шифровальщик = new LicenseCrypto("машина");

        Assert.Throws<CryptographicException>(() => шифровальщик.Unprotect(new byte[] { 1, 2, 3 }));
    }
}
