# Первый запуск серверной части Picket302: проверка здоровья службы и
# выдача одноразового кода сопряжения для клиента.
#
# Требования: PowerShell 5.1+, служба установлена и запущена, сценарий
# выполняется на серверном компьютере (служебные маршруты доступны только
# с локального адреса и с административным токеном).

$ErrorActionPreference = "Stop"

$Port = 30242
$BaseUrl = "http://127.0.0.1:$Port"
$TokenFile = "$env:ProgramData\Picket302\Service\admin.token"

if (-not (Test-Path $TokenFile)) {
    Write-Error "Административный токен не найден: $TokenFile. Служба не запускалась?"
    exit 1
}

$Token = (Get-Content $TokenFile -Raw).Trim()
$Headers = @{ "X-Picket302-Admin-Token" = $Token }

# 1. Проверка здоровья службы.
Write-Host "Проверка здоровья службы на $BaseUrl ..."

try {
    $Health = Invoke-RestMethod -Uri "$BaseUrl/api/v1/health" -Method Get -TimeoutSec 10
} catch {
    Write-Error "Служба не отвечает: $($_.Exception.Message)"
    exit 1
}

Write-Host ("Служба готова. Версия протокола: {0}. Идентификатор сервера: {1}." -f `
    $Health.protocolVersion, $Health.serverId)

# 2. Выдача одноразового кода сопряжения (действует ограниченное время и
#    ровно для одного подключения; нельзя получить удалённым запросом).
Write-Host ""
Write-Host "Выдача одноразового кода сопряжения..."

$КодОтвет = Invoke-RestMethod `
    -Uri "$BaseUrl/api/v1/admin/pairing-code" `
    -Method Post `
    -Headers $Headers `
    -TimeoutSec 10

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
