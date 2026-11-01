using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Вложения электронной рассылки (ТЗ §8.7, функция 98: копирование в
/// хранилище отправителя; предупреждение о файлах более 25 МБ).
/// </summary>
public sealed class AttachmentServiceTests : IDisposable
{
    private readonly string _каталог =
        Path.Combine(Path.GetTempPath(), "Picket302Attachments_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Создаёт тестовый каталог хранилища.
    /// </summary>
    public AttachmentServiceTests()
    {
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_каталог))
        {
            Directory.Delete(_каталог, recursive: true);
        }
    }

    [Fact]
    public void ДобавлениеВложения_КопируетФайлВХранилище()
    {
        var хранилище = Path.Combine(_каталог, "Attachments");
        var служба = new AttachmentService(хранилище);
        var исходный = Path.Combine(_каталог, "исходник.txt");

        File.WriteAllText(исходный, "содержимое вложения");

        var рассылка = new EmailDistribution { Name = "Рассылка" };

        служба.AddAttachment(рассылка, исходный);

        var вложение = Assert.Single(рассылка.Attachments);

        // Копия лежит в хранилище отправителя, а не по исходному пути.
        Assert.StartsWith(хранилище, вложение, StringComparison.Ordinal);
        Assert.True(File.Exists(вложение));
        Assert.Equal("содержимое вложения", File.ReadAllText(вложение));

        // Удаление исходного файла не ломает повторную отправку (ТЗ §8.7, ф.98).
        File.Delete(исходный);

        Assert.Empty(служба.CollectWarnings(рассылка));
    }

    [Fact]
    public void ВложениеБолее25Мб_ДаПредупреждение()
    {
        var хранилище = Path.Combine(_каталог, "Attachments");
        var служба = new AttachmentService(хранилище);
        var большой = Path.Combine(_каталог, "большой.bin");

        // Разреженный файл объёмом «25 МБ + 1 байт».
        using (var поток = File.Create(большой))
        {
            поток.SetLength(AttachmentService.МаксимальныйРазмерВложенияБайт + 1);
        }

        var рассылка = new EmailDistribution { Name = "Рассылка" };

        служба.AddAttachment(рассылка, большой);

        var предупреждение = Assert.Single(служба.Warnings);

        Assert.Contains("25", предупреждение, StringComparison.Ordinal);
        Assert.Contains("превышает", предупреждение, StringComparison.Ordinal);
    }

    [Fact]
    public void ОтсутствующийФайлВХранилище_ПредупреждениеПриОтправке()
    {
        var хранилище = Path.Combine(_каталог, "Attachments");
        var служба = new AttachmentService(хранилище);
        var рассылка = new EmailDistribution { Name = "Рассылка" };

        рассылка.Attachments.Add(Path.Combine(хранилище, "потерян.txt"));

        var предупреждение = Assert.Single(служба.CollectWarnings(рассылка));

        Assert.Contains("не найден в хранилище", предупреждение, StringComparison.Ordinal);
    }

    [Fact]
    public void НесуществующийИсходник_Отказ()
    {
        var служба = new AttachmentService(Path.Combine(_каталог, "Attachments"));

        Assert.Throws<FileNotFoundException>(
            () => служба.AddAttachment(new EmailDistribution(), Path.Combine(_каталог, "нет.txt")));
    }
}
