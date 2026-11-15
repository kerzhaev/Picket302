# Первый запуск серверной части Picket302: проверка здоровья службы и
# выдача одноразового кода сопряжения для клиента.
#
# Требования: PowerShell 5.1+ (Windows PowerShell либо PowerShell 7),
# служба установлена и запущена, сценарий выполняется на серверном
# компьютере (служебные маршруты доступны только с локального адреса и
# с административным токеном).
#
# Канал: служба работает только по TLS, поэтому консоль обращается к ней
# по https://127.0.0.1. Сертификат службы проверяется: принимается ровно
# тот сертификат, отпечаток которого указан в service.json и найден в
# хранилище сертификатов компьютера. Исключения «доверять любому
# сертификату» нет: даже локальная консоль не должна молча принять
# подменённый узел.

param(
    # Каталог данных службы, если он перенесён из %ProgramData%
    # (настраивается в ServiceOptions.DataDirectory).
    [string]$КаталогДанных = "$env:ProgramData\Picket302\Service"
)

$ErrorActionPreference = "Stop"

$SettingsFile = "$КаталогДанных\service.json"
$TokenFile = "$КаталогДанных\admin.token"

# 1. Настройки службы: отпечаток сертификата сервера.
if (-not (Test-Path $SettingsFile)) {
    Write-Error ("Файл настроек службы не найден: {0}. Сертификат сервера не настроен — см. раздел «Подготовка сертификата» в installer\README.md." -f $SettingsFile)
    exit 1
}

$Настройки = Get-Content $SettingsFile -Raw | ConvertFrom-Json

# Порт службы: настраивается в service.json (ServiceOptions.Port); по
# умолчанию — стандартный порт совместного режима.
$Port = 30242
if ($Настройки.PSObject.Properties["Port"]) {
    $разобранныйПорт = 0
    if ([int]::TryParse([string]$Настройки.Port, [ref]$разобранныйПорт) -and $разобранныйПорт -ge 1 -and $разобранныйПорт -le 65535) {
        $Port = $разобранныйПорт
    }
}

$BaseUrl = "https://127.0.0.1:$Port"

$Отпечаток = $Настройки.CertificateThumbprint
if ([string]::IsNullOrWhiteSpace($Отпечаток)) {
    $Отпечаток = $Настройки.certificateThumbprint
}

if ([string]::IsNullOrWhiteSpace($Отпечаток)) {
    Write-Error ("В {0} не указан сертификат сервера (CertificateThumbprint). Служба без сертификата не запускается: секрет устройства не передаётся по открытому каналу. См. installer\README.md, раздел «Подготовка сертификата»." -f $SettingsFile)
    exit 1
}

$Отпечаток = $Отпечаток.Trim()
$Сертификат = Get-Item "Cert:\LocalMachine\My\$Отпечаток" -ErrorAction SilentlyContinue

if ($null -eq $Сертификат) {
    Write-Error ("Сертификат с отпечатком {0} не найден в хранилище Cert:\LocalMachine\My. Выполните подготовку сертификата (installer\README.md) и перезапустите службу." -f $Отпечаток)
    exit 1
}

# Административный токен: выдача кода сопряжения требует локального
# запроса с этим токеном.
if (-not (Test-Path $TokenFile)) {
    Write-Error ("Административный токен не найден: {0}. Служба не запускалась?" -f $TokenFile)
    exit 1
}

$Token = (Get-Content $TokenFile -Raw).Trim()

# 2. HTTP-клиент с закреплением сертификата службы.
Add-Type -AssemblyName System.Net.Http

$ОжидаемыйОтпечаток = $Сертификат.Thumbprint

# Проверка сертификата выполняется скомпилированным делегатом, а не
# скриптовым блоком PowerShell: обработчик вызывается .NET из фонового
# потока, где пространства выполнения PowerShell нет, и скриптовый блок
# приводит к ошибке «нет доступного пространства выполнения».
$ИсходникВалидатора = @'
using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public static class Picket302CertificatePin
{
    public static string Expected = string.Empty;

    private static bool Совпадает(X509Certificate сертификат)
    {
        if (сертификат == null)
        {
            return false;
        }

        using (var cert = new X509Certificate2(сертификат))
        {
            return string.Equals(cert.Thumbprint, Expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool ValidateRemote(
        object отправитель,
        X509Certificate сертификат,
        X509Chain цепочка,
        SslPolicyErrors ошибки)
    {
        return Совпадает(сертификат);
    }

    public static RemoteCertificateValidationCallback RemoteCallback
    {
        get { return ValidateRemote; }
    }
}
'@

# Делегат объявляется в C#: приведение ссылки на метод в PowerShell к типу
# делегата не поддерживается.
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = $null

Add-Type -TypeDefinition $ИсходникВалидатора

[Picket302CertificatePin]::Expected = $ОжидаемыйОтпечаток

# TLS 1.2 задаётся всегда: версия протокола не должна зависеть от настроек
# системы по умолчанию.
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

# Глобальный обработчик: работает в .NET Framework и вызывается из фонового
# потока. Доверия «любому сертификату» нет — принимается ровно сертификат с
# указанным в service.json отпечатком.
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = [Picket302CertificatePin]::RemoteCallback

$Обработчик = [System.Net.Http.HttpClientHandler]::new()

$ИсходникОбработчика = @'
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public static class Picket302CertificatePinHttp
{
    public static string Expected = string.Empty;

    private static bool Совпадает(X509Certificate сертификат)
    {
        if (сертификат == null)
        {
            return false;
        }

        using (var cert = new X509Certificate2(сертификат))
        {
            return string.Equals(cert.Thumbprint, Expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool Validate2(
        HttpRequestMessage запрос,
        X509Certificate2 сертификат,
        X509Chain цепочка,
        SslPolicyErrors ошибки)
    {
        return Совпадает(сертификат);
    }

    public static Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> Callback2
    {
        get { return Validate2; }
    }

    public static bool Validate1(
        HttpRequestMessage запрос,
        X509Certificate сертификат,
        X509Chain цепочка,
        SslPolicyErrors ошибки)
    {
        return Совпадает(сертификат);
    }

    public static Func<HttpRequestMessage, X509Certificate, X509Chain, SslPolicyErrors, bool> Callback1
    {
        get { return Validate1; }
    }
}
'@

try {
    Add-Type -TypeDefinition $ИсходникОбработчика -ReferencedAssemblies 'System.Net.Http'
    [Picket302CertificatePinHttp]::Expected = $ОжидаемыйОтпечаток

    try {
        $Обработчик.ServerCertificateCustomValidationCallback = [Picket302CertificatePinHttp]::Callback2
    }
    catch {
        $Обработчик.ServerCertificateCustomValidationCallback = [Picket302CertificatePinHttp]::Callback1
    }
}
catch {
    # .NET Framework: глобальный обработчик ServicePointManager уже действует.
}

$Клиент = [System.Net.Http.HttpClient]::new($Обработчик)
$Клиент.Timeout = [TimeSpan]::FromSeconds(10)

# 3. Проверка здоровья службы.
Write-Host "Проверка здоровья службы на $BaseUrl ..."

try {
    $Здоровье = ($Клиент.GetStringAsync("$BaseUrl/api/v1/health").GetAwaiter().GetResult()) | ConvertFrom-Json
}
catch {
    Write-Error ("Служба не отвечает: {0}" -f $_.Exception.Message)
    exit 1
}

Write-Host ("Служба готова. Версия протокола: {0}. Идентификатор сервера: {1}." -f $Здоровье.protocolVersion, $Здоровье.serverId)

# Версия протокола берётся из health-ответа службы: запросы к маршрутам
# команд и администрирования без заголовка версии служба отклоняет (426).
$ВерсияПротокола = [string]$Здоровье.protocolVersion
if ([string]::IsNullOrWhiteSpace($ВерсияПротокола)) {
    $ВерсияПротокола = "1"
}

# 4. Выдача одноразового кода сопряжения (действует ограниченное время и
# ровно для одного подключения; нельзя получить удалённым запросом).
Write-Host ""
Write-Host "Выдача одноразового кода сопряжения..."

$ЗапросКода = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$BaseUrl/api/v1/admin/pairing-code")
$ЗапросКода.Headers.Add("X-Picket302-Protocol-Version", $ВерсияПротокола)
$ЗапросКода.Headers.Add("X-Picket302-Admin-Token", $Token)

try {
    $ОтветКода = $Клиент.SendAsync($ЗапросКода).GetAwaiter().GetResult()
}
catch {
    Write-Error ("Служба не отвечает: {0}" -f $_.Exception.Message)
    exit 1
}

if (-not $ОтветКода.IsSuccessStatusCode) {
    Write-Error ("Служба отклонила выдачу кода сопряжения: HTTP {0}. Проверьте, что сценарий выполняется на серверном компьютере, а административный токен актуален." -f [int]$ОтветКода.StatusCode)
    exit 1
}

$КодОтвет = ($ОтветКода.Content.ReadAsStringAsync().GetAwaiter().GetResult()) | ConvertFrom-Json

Write-Host ""
Write-Host "=========================================================="
Write-Host "  КОД СОПРЯЖЕНИЯ (одноразовый):  $($КодОтвет.code)"
Write-Host "  Действует до:                  $($КодОтвет.expiresAt) (UTC)"
Write-Host ""
Write-Host "  Отпечаток сервера (показать клиенту для сверки):"
Write-Host "  $($КодОтвет.serverFingerprint)"
Write-Host "=========================================================="
Write-Host ""
Write-Host "Введите этот код в клиенте Picket302: Сервис - Общая база..."
Write-Host "Клиент должен подключиться по адресу этого компьютера и порту $Port."

$Клиент.Dispose()
