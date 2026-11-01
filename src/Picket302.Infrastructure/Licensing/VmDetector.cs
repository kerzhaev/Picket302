using System.Security;
using Microsoft.Win32;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Мягкое определение виртуальной машины (ТЗ §8.10, функция 121:
/// «Отдельная проверка признаков виртуализации (по данным WMI/BIOS).
/// Обязательное требование — мягкая деградация: детект ВМ вызывает
/// предупреждение или демо-режим, но не блокирует запуск и не использует
/// низкоуровневые приёмы»).
/// </summary>
/// <remarks>
/// <para>
/// Источники признаков — ветка реестра
/// <c>HKLM\HARDWARE\DESCRIPTION\System\BIOS</c> (производитель, модель,
/// версия видео-BIOS) и наличие служебных веток реестра средств виртуализации.
/// Обращения к портам ввода-вывода, перехват отладчика и другие приёмы
/// самозащиты процесс не выполняет (ТЗ §8.10, функция 125; §10.7 — явный
/// запрет агрессивной самозащиты процесса).
/// </para>
/// <para>
/// Определить не удалось — машина считается физической, в журнал пишется
/// соответствующее сообщение (ТЗ §8.10, функция 121).
/// </para>
/// </remarks>
public sealed class VmDetector
{
    private static readonly string[] ПризнакиВиртуализации =
    [
        "Virtual",
        "VMware",
        "QEMU",
        "KVM",
        "Hyper-V",
        "HyperV",
        "Xen",
        "Parallels",
        "VirtualBox",
        "Bochs",
        "XenProject",
        "Virtual Machine",
        "vbox",
        "innotek",
        "Oracle VM",
    ];

    private static readonly string[] СлужебныеВеткиРеестра =
    [
        @"SOFTWARE\VMware, Inc.",
        @"SOFTWARE\Oracle\VirtualBox Guest Additions",
        @"SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters",
        @"SYSTEM\CurrentControlSet\Services\vmmouse",
        @"SYSTEM\CurrentControlSet\Services\vmci",
    ];

    private static readonly string[] ИменаПараметровBios =
    [
        "SystemManufacturer",
        "SystemProductName",
        "SystemFamily",
        "SystemSKU",
        "VideoBiosVersion",
    ];

    private readonly Action<string>? _журнал;

    /// <summary>
    /// Создаёт определитель виртуальной машины.
    /// </summary>
    /// <param name="журнал">
    /// Приёмник сообщений о невозможности определить признаки виртуализации;
    /// <see langword="null"/> — сообщения не фиксируются.
    /// </param>
    public VmDetector(Action<string>? журнал = null)
    {
        _журнал = журнал;
    }

    /// <summary>
    /// Определяет, работает ли программа в виртуальной машине
    /// (ТЗ §8.10, функция 121: <c>public bool IsVirtualMachine()</c>).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> — признаки виртуализации обнаружены;
    /// <see langword="false"/> — признаков нет либо определить не удалось.
    /// </returns>
    public bool IsVirtualMachine()
    {
        try
        {
            using (var ветка = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            {
                foreach (var имя in ИменаПараметровBios)
                {
                    if (ветка?.GetValue(имя) is not string значение || string.IsNullOrWhiteSpace(значение))
                    {
                        continue;
                    }

                    foreach (var признак in ПризнакиВиртуализации)
                    {
                        if (значение.Contains(признак, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (var путь in СлужебныеВеткиРеестра)
            {
                using var ветка = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(путь);

                if (ветка is not null)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception исключение) when (исключение is UnauthorizedAccessException
            or SecurityException
            or IOException)
        {
            _журнал?.Invoke(
                "Не удалось определить признаки виртуальной машины: " + исключение.Message
                + " Машина считается физической.");

            return false;
        }
    }
}
