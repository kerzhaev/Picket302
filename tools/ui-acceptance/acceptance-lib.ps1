# Общая библиотека acceptance-драйвера Picket302 (fail-closed):
# - запуск harness с явным профилем; фоновое чтение stdout;
# - ожидание окон по заголовку; BM_CLICK кнопок;
# - отзывчивость UI через SendMessageTimeout WM_NULL;
# - fingerprint реального профиля через [Environment]::GetFolderPath
#   (переменная APPDATA не используется — она может отличаться от
#   фактического пути Known Folder).
# Fail-closed: $ErrorActionPreference = 'Stop' — любая ошибка cmdlet-а
# превращается в исключение и ненулевой код возврата драйвера; никакие
# шаги не заглушаются SilentlyContinue.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

public static class AccWin
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc proc, IntPtr lp);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hwnd, EnumProc proc, IntPtr lp);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int x1, int y1, int rop);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> WindowsOf(uint pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, lp) =>
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string Title(IntPtr h)
    {
        var t = new StringBuilder(512);
        GetWindowText(h, t, 512);
        return t.ToString();
    }

    public static IntPtr FindByTitle(uint pid, string title)
    {
        foreach (var h in WindowsOf(pid))
        {
            if (Title(h) == title) return h;
        }
        return IntPtr.Zero;
    }

    public static IntPtr FindButton(IntPtr dialog, string text)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dialog, (h, lp) =>
        {
            if (found != IntPtr.Zero) return true;
            var cls = new StringBuilder(256);
            GetClassName(h, cls, 256);
            var c = cls.ToString();
            // WinForms-кнопки и нативные кнопки MessageBox (класс Button).
            if (!c.StartsWith("WindowsForms10.Button", StringComparison.OrdinalIgnoreCase) && c != "Button") return true;
            if (Title(h) == text) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Click(IntPtr hwnd)
    {
        PostMessage(hwnd, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
    }

    public static bool Responsive(IntPtr hwnd, uint timeoutMs)
    {
        IntPtr res;
        var ok = SendMessageTimeout(hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero, 0x0008, timeoutMs, out res);
        return ok != IntPtr.Zero;
    }

    public static void Capture(IntPtr hwnd, string path)
    {
        RECT r;
        GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;
        using (var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc();
                IntPtr src = GetWindowDC(hwnd);
                BitBlt(dst, 0, 0, w, h, src, 0, 0, 0x00CC0020);
                ReleaseDC(hwnd, src);
                g.ReleaseHdc(dst);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@

function Get-ApplicationDataFolder {
    # Known Folder ApplicationData: единственный достоверный источник
    # (переменная окружения APPDATA может отсутствовать или отличаться).
    return [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
}

function Test-ProfileFingerprint([string]$dir) {
    # Отпечаток профиля — fail-closed: ошибка перечисления каталога или
    # чтения файла бросает исключение (частичный отпечаток невозможен).
    # -Force включает скрытые файлы: невидимый для перечисления файл
    # мог бы измениться незамеченным.
    $map = @{}
    if (-not (Test-Path $dir)) { return $map }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        Get-ChildItem -Recurse -File -Force $dir | ForEach-Object {
            $байты = $хеш.ComputeHash([System.IO.File]::ReadAllBytes($_.FullName))
            $sha = [System.BitConverter]::ToString($байты).Replace('-', '')
            $map[$_.FullName] = "$sha|$($_.LastWriteTimeUtc.Ticks)"
        }
    } finally { $хеш.Dispose() }
    return $map
}

function Compare-Fingerprints($before, $after) {
    $новые = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
    $изменённые = @($after.Keys | Where-Object { $before.ContainsKey($_) -and $before[$_] -ne $after[$_] })
    $удалённые = @($before.Keys | Where-Object { -not $after.ContainsKey($_) })
    return @{ New = $новые; Changed = $изменённые; Removed = $удалённые }
}

function Get-SentinelState([string]$path) {
    if (-not (Test-Path $path)) { return '<absent>' }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        $байты = $хеш.ComputeHash([System.IO.File]::ReadAllBytes($path))
        $sha = [System.BitConverter]::ToString($байты).Replace('-', '')
        return "$sha|$( (Get-Item $path).LastWriteTimeUtc.Ticks )"
    } finally { $хеш.Dispose() }
}

# Запускает harness. stdout читается инкрементально на вызывающем потоке
# через чистый .NET ReadLineAsync (PowerShell-замыкания в фоновых потоках
# обращаются к SessionState и взаимно блокируются).
function Start-Acceptance([string]$exe, [string]$mode, [string]$profileRoot, [bool]$keep = $false) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "$mode `"$profileRoot`"" + $(if ($keep) { " keep" } else { "" })
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $p = [System.Diagnostics.Process]::Start($psi)

    return @{
        Proc = $p
        Lines = [System.Collections.ArrayList]::new()
    }
}

# Читает stdout harness построчно (ReadLineAsync) до строки, совпавшей с
# $pattern, либо до таймаута/закрытия потока. Все прочитанные строки
# накапливаются в $h.Lines. Возвращает $true при совпадении.
function Read-HarnessLines($h, [string]$pattern, [int]$seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)

    while ((Get-Date) -lt $deadline) {
        $задача = $h.Proc.StandardOutput.ReadLineAsync()

        while (-not $задача.IsCompleted) {
            if ((Get-Date) -ge $deadline -or $h.Proc.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }

        if (-not $задача.IsCompleted) { return $false }

        $строка = $задача.Result

        if ($null -eq $строка) { return $false }  # stdout закрыт

        [void]$h.Lines.Add($строка)

        if ($строка -match $pattern) { return $true }
    }

    return $false
}

# Возвращает все накопленные строки stdout harness.
function Get-HarnessLines($h) {
    return @($h.Lines.ToArray())
}

# Штатное завершение: graceful CLOSE, при таймауте — единый движок
# Stop-HarnessTree (Вариант A). Stop-Acceptance сам НЕ убивает процесс:
# вся force-логика и проверка дерева — в Stop-HarnessTree.
# Возвращает структурированный результат остановки:
#   Pid               — PID harness (сохранён до любых попыток завершения);
#   Graceful          — завершился штатно (по CLOSE или уже был завершён);
#   Killed            — потребовался force (для драйвера — FAIL);
#   TreeOk            — дерево проверено и чисто;
#   TreeConfirmed     — проверка дерева выполнена (CIM-ошибки = false);
#   RemainingPids     — живые процессы дерева после cleanup;
#   UnconfirmedPids   — потомки с неподтверждённой идентичностью (PID reuse);
#   TreeError         — ошибки taskkill/CIM/проверки;
#   KillOutput        — stdout/stderr taskkill для диагностики.
function Stop-Acceptance($h) {
    if ($h -eq $null -or $h.Proc -eq $null) {
        $результат = New-StopResult
        $результат.TreeError = 'harness не запускался (null)'
        return $результат
    }

    return Stop-HarnessTree $h.Proc 20000
}

function New-StopResult {
    return @{
        Pid = 0
        Graceful = $false
        Killed = $false
        TreeOk = $false
        TreeConfirmed = $false
        RemainingPids = @()
        UnconfirmedPids = @()
        TreeError = ''
        KillOutput = @()
        StartTimeKnown = $false
    }
}

function Wait-AppWindow([uint32]$procId, [string]$title, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = [AccWin]::FindByTitle($procId, $title)
        if ($w -ne [IntPtr]::Zero) { return $w }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

function Wait-Button([IntPtr]$dialog, [string]$text, [int]$seconds = 8) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $b = [AccWin]::FindButton($dialog, $text)
        if ($b -ne [IntPtr]::Zero) { return $b }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

function Dump-AllWindows([uint32]$procId) {
    $список = @()
    foreach ($w in [AccWin]::WindowsOf($procId)) {
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($w, $cls, 256) | Out-Null
        $список += ($cls.ToString() + " | '" + [AccWin]::Title($w) + "'")
    }
    return $список
}

# Единственный контролируемый путь записи evidence-файла прогона.
# Вызывается ОДИН раз после завершения cleanup и фиксации финальных
# статусов; возвращает $true при успешной записи (неудача записи —
# сигнал драйверу выставить exit 1).
function ЗаписатьEvidence([string]$относительныйПуть, [string]$заголовок, [string]$режимСтрока, $журнал) {
    try {
        $каталог = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
        $строки = @(
            "# $заголовок",
            '',
            "Прогон: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), профиль: временный (guid), $режимСтрока.",
            ''
        ) + @($журнал)
        Set-Content -Path (Join-Path $каталог $относительныйПуть) -Value $строки -Encoding UTF8 -ErrorAction Stop
        return $true
    } catch {
        Write-Host "  EVIDENCE WRITE FAIL: $($_.Exception.Message)"
        return $false
    }
}

# --- Межпроцессная блокировка общих ресурсов тестовой инфраструктуры ---
# Named mutex с тем же идентификатором, что и C#-хелпер
# БлокировкаОбщихРесурсов (tests/.../БлокировкаОбщихРесурсов.cs): защищает
# реальный профиль %AppData%\Picket302, sentinel, шаблон
# %TEMP%\P302GuardTest_* и acceptance-каталоги между НЕЗАВИСИМЫМИ
# процессами (параллельный testhost, ручной прогон, CI-агент).
# Таймаут ограничен, отказ — fail-closed; AbandonedMutexException
# (владелец умер) обрабатывается с диагностикой — мьютекс при этом получен.
function Enter-SharedTestLock([int]$seconds = 180) {
    $мьютекс = New-Object System.Threading.Mutex($false, 'Local\Picket302.TestInfrastructure.SharedProfile')
    $получен = $false
    try {
        $получен = $мьютекс.WaitOne([TimeSpan]::FromSeconds($seconds))
    } catch [System.Threading.AbandonedMutexException] {
        Write-Host '  LOCK: предыдущий владелец умер, не освободив мьютекс (abandoned) — блокировка получена, ресурс мог остаться неубранным'
        $получен = $true
    }
    if (-not $получен) {
        $мьютекс.Dispose()
        throw ("FAIL: общая блокировка тестовой инфраструктуры не получена за ${seconds} с — " +
            'общий ресурс занят другим процессом (параллельный прогон). Продолжение запрещено (fail-closed).')
    }
    return $мьютекс
}

# Освобождение межпроцессной блокировки — fail-closed контракт: функция
# возвращает структурированный результат и НИКОГДА не считает
# непроверенное освобождение успешным:
#   Attempted — попытка освобождения выполнена ($null = захват не
#               выполнялся — это failure вызывающего кода, не успех);
#   Released  — ReleaseMutex подтверждён;
#   Disposed  — Dispose мьютекса подтверждён;
#   Error     — диагностика: имя мьютекса, PID, сценарий, исключение.
# Двойной вызов даёт Released=$false (повторный ReleaseMutex невозможен) —
# ложного PASS нет. Fault-инъекция (только тестовый режим):
# P302_LOCK_FAULT=release-fail | dispose-fail.
function Exit-SharedTestLock($мьютекс, [string]$сценарий = '') {
    $результат = @{ Attempted = $false; Released = $false; Disposed = $false; Error = '' }
    $имяМьютекса = 'Local\Picket302.TestInfrastructure.SharedProfile'

    if ($мьютекс -eq $null) {
        $результат.Error = ('блокировка не захватывалась (передан $null) — ' +
            "mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
        Write-Host ("  LOCK-FAIL: " + $результат.Error)
        return $результат
    }

    $результат.Attempted = $true

    $fault = $env:P302_LOCK_FAULT

    if ($fault -eq 'release-fail') {
        $результат.Error = ('fault-inject: lock release failure | ' +
            "mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
        Write-Host "  FAULT-INJECT: lock release failure"
    } elseif ($fault -eq 'dispose-fail') {
        try {
            $мьютекс.ReleaseMutex()
            $результат.Released = $true
        } catch {
            $результат.Error = ('ReleaseMutex: ' + $_.Exception.GetType().Name + ': ' +
                $_.Exception.Message + " | mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
            Write-Host ("  LOCK-FAIL: " + $результат.Error)
        }

        $результат.Error = ('fault-inject: mutex dispose failure | ' +
            "mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
        Write-Host "  FAULT-INJECT: mutex dispose failure"
        return $результат
    } else {
        try {
            $мьютекс.ReleaseMutex()
            $результат.Released = $true
        } catch {
            $результат.Error = ('ReleaseMutex: ' + $_.Exception.GetType().Name + ': ' +
                $_.Exception.Message + " | mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
            Write-Host ("  LOCK-FAIL: " + $результат.Error)
        }
    }

    try {
        $мьютекс.Dispose()
        $результат.Disposed = $true
    } catch {
        $сообщение = ('Dispose: ' + $_.Exception.GetType().Name + ': ' +
            $_.Exception.Message + " | mutex=$имяМьютекса | pid=$PID | сценарий=$сценарий")
        if ($результат.Error) {
            $результат.Error = $результат.Error + '; ' + $сообщение
        } else {
            $результат.Error = $сообщение
        }
        Write-Host ("  LOCK-FAIL: " + $сообщение)
    }

    return $результат
}

# --- Дерево процессов: сбор потомков, проверка, принудительная уборка ---

# Рекурсивно собирает потомков процесса. Ошибка CIM — ИСКЛЮЧЕНИЕ
# (fail-closed), а не пустой список: отсутствие данных не считается
# «дерево чисто». Идентичность против PID reuse: потомок считается
# нашим только при известном времени старта предка и собственном
# CreationDate не старше старта корневого предка (секундный запас —
# гранулярность часов CIM и .NET); неподтверждённые не убиваются
# и возвращаются с Confirmed=$false (fail-closed диагностика).
# Fault-инъекция (только тестовый режим): P302_TREE_FAULT=cim-fail.
function Get-ProcessDescendants([int]$ancestorPid, [datetime]$ancestorStart, [bool]$startKnown) {
    if ($env:P302_TREE_FAULT -eq 'cim-fail') {
        throw 'fault-inject: CIM tree enumeration failure'
    }

    $найденные = @()

    try {
        $записи = @(Get-CimInstance Win32_Process `
            -Filter "ParentProcessId=$ancestorPid" `
            -Property ProcessId,ParentProcessId,Name,CreationDate `
            -ErrorAction Stop)
    } catch {
        throw ("SAFETY-FAIL: перечисление дерева процессов не выполнено " +
            "(ParentProcessId=$ancestorPid): " + $_.Exception.Message)
    }

    foreach ($запись in $записи) {
        $подтверждён = $startKnown -and $null -ne $запись.CreationDate -and $запись.CreationDate -ge $ancestorStart.AddSeconds(-1)
        $найденные += @{ Pid = [int]$запись.ProcessId; Name = $запись.Name; Confirmed = $подтверждён }

        if ($подтверждён) {
            # Рекурсия вглубь: прямых детей недостаточно, если внутри
            # harness есть более глубокое дерево.
            $найденные += Get-ProcessDescendants $запись.ProcessId $ancestorStart $startKnown
        }
    }

    return $найденные
}

# Проверяет жив ли процесс по PID.
function Test-ProcessAlive([int]$идентификатор) {
    try {
        return $null -ne (Get-Process -Id $идентификатор -ErrorAction Stop)
    } catch {
        return $false
    }
}

# Пост-проверка дерева: процесс завершился и живых подтверждённых
# потомков нет; ошибки CIM и неподтверждённые потомки — failure
# (fail-closed). Ничего не убивает. Fault (только тестовый режим):
# P302_GUARD_FAULT=tree-cleanup-fail.
function Test-ProcessTreeGone($proc) {
    $результат = @{
        Pid = 0
        Ok = $false
        RemainingPids = @()
        UnconfirmedPids = @()
        Error = ''
    }

    if ($proc -eq $null) {
        $результат.Error = 'процесс не запускался (null)'
        return $результат
    }

    try {
        $результат.Pid = $proc.Id
    } catch {
        $результат.Error = "PID недоступен: $($_.Exception.Message)"
        return $результат
    }

    if ($env:P302_GUARD_FAULT -eq 'tree-cleanup-fail') {
        $результат.Error = 'fault-inject: tree verification failure'
        Write-Host "  FAULT-INJECT: tree verification failure"
        return $результат
    }

    try {
        $старт = $proc.StartTime
    } catch {
        $старт = [datetime]::MinValue
    }

    if (-not $proc.HasExited) {
        $результат.RemainingPids = @($результат.Pid)
        $результат.Error = 'процесс ещё жив'
        return $результат
    }

    try {
        $потомки = @(Get-ProcessDescendants $результат.Pid $старт ($старт -ne [datetime]::MinValue))
    } catch {
        $результат.Error = $_.Exception.Message
        return $результат
    }

    $результат.UnconfirmedPids = @($потомки | Where-Object { -not $_.Confirmed } | ForEach-Object { $_.Pid })
    $результат.RemainingPids = @($потомки | Where-Object { $_.Confirmed } | Where-Object { Test-ProcessAlive $_.Pid } | ForEach-Object { $_.Pid })

    if ($результат.RemainingPids.Count -gt 0) {
        $результат.Error = 'живые потомки дерева: ' + ($результат.RemainingPids -join '; ')
    }

    if ($результат.UnconfirmedPids.Count -gt 0 -and -not $результат.Error) {
        $результат.Error = 'потомки с неподтверждённой идентичностью (PID reuse риск): ' + ($результат.UnconfirmedPids -join '; ')
    }

    $результат.Ok = ($результат.Error -eq '')
    return $результат
}

# Единый движок аварийной уборки дерева (Вариант A):
#   1) PID и StartTime сохраняются ДО любых попыток завершения;
#   2) graceful CLOSE, при таймауте — taskkill /T /F + Kill;
#   3) $LASTEXITCODE taskkill проверяется; «процесс уже завершён»
#      (код 128) отличён от реальной ошибки;
#   4) потомки собираются РЕКУРСИВНО; ошибка CIM — fail-closed
#      (не пустой список);
#   5) отдельный повторный скан после завершения; подтверждённые
#      остатки добиваются, затем третий скан;
#   6) оставшиеся PID / неподтверждённые / ошибки проверки → TreeOk=$false
#      (драйвер обязан выставить fail-флаги и exit 1).
# Fault-инъекции (только тестовый режим): P302_TREE_FAULT=kill-fail |
# cim-fail | descendant-remains.
function Stop-HarnessTree($proc, [int]$waitMs = 20000) {
    $результат = New-StopResult

    if ($proc -eq $null) {
        $результат.TreeError = 'процесс не запускался (null)'
        return $результат
    }

    try {
        $результат.Pid = $proc.Id
    } catch {
        $результат.TreeError = "PID недоступен: $($_.Exception.Message)"
        return $результат
    }

    $пид = $результат.Pid

    # 1. Идентичность ДО любых попыток завершения (PID reuse).
    try {
        $старт = $proc.StartTime
        $результат.StartTimeKnown = $true
    } catch {
        $старт = [datetime]::MinValue
        $результат.StartTimeKnown = $false
        Write-Host "  TREE: StartTime недоступен заранее (pid=$пид) — идентичность потомков не подтвердить, проверка fail-closed"
    }

    # 2. Graceful, если жив.
    if (-not $proc.HasExited) {
        if ($proc.StandardInput -ne $null) {
            try {
                $proc.StandardInput.WriteLine('CLOSE')
            } catch {
                Write-Host "  CLOSE не доставлен (процесс завершается): $($_.Exception.Message)"
            }
        }

        if ($proc.WaitForExit($waitMs)) {
            $результат.Graceful = $true
            Write-Host "  TREE: штатное завершение подтверждено (pid=$пид) — следует проверка дерева"
        } else {
            # 3. Force: taskkill /T /F с обязательной проверкой кода.
            Write-Host "  HARNESS KILL: штатное завершение не сработало (pid=$пид) — taskkill /T /F"
            $кодTaskkill = 1
            $выводForce = @()

            if ($env:P302_TREE_FAULT -eq 'kill-fail') {
                $кодTaskkill = 1
                $выводForce = @('fault-inject: taskkill failure simulated')
                $результат.TreeError = "taskkill вернул 1 (pid=$пид): fault-inject: taskkill failure simulated"
            } else {
                $выводForce = & taskkill /T /F /PID $пид 2>&1
                $кодTaskkill = $LASTEXITCODE

                try { $proc.Kill() } catch {
                    if (-not $proc.HasExited) {
                        Write-Host "  HARNESS KILL: Kill не выполнен: $($_.Exception.Message)"
                    }
                }
                try { $proc.WaitForExit(10000) | Out-Null } catch {}
            }

            $результат.KillOutput = @($выводForce)
            $результат.Killed = $true

            if ($кодTaskkill -ne 0 -and $кодTaskkill -ne 128) {
                # 128 = «процесс не найден» — уже завершён (безопасный
                # случай); остальные коды — реальная ошибка.
                $сообщение = "taskkill вернул $кодTaskkill (pid=$пид): $($выводForce -join ' | ')"
                if ($результат.TreeError) {
                    $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                } else {
                    $результат.TreeError = $сообщение
                }
                Write-Host "  SAFETY-FAIL: $сообщение"
            }

            if (-not $proc.HasExited) {
                Write-Host "  SAFETY-FAIL: процесс не завершён после Kill (pid=$пид)"
            }
        }
    } else {
        # Родитель уже завершён к моменту уборки: «родитель завершился»
        # НЕ доказательство чистоты дерева — потомки проверяются ниже по
        # сохранённому PID.
        $результат.Graceful = $true
    }

    # 4. Первый скан дерева (рекурсивно; CIM-ошибка — fail-closed).
    $потомки = @()
    $сканОк = $false

    try {
        $потомки = @(Get-ProcessDescendants $пид $старт $результат.StartTimeKnown)
        $сканОк = $true
    } catch {
        $сообщение = $_.Exception.Message
        if ($результат.TreeError) {
            $результат.TreeError = $результат.TreeError + '; ' + $сообщение
        } else {
            $результат.TreeError = $сообщение
        }
    }

    if ($сканОк) {
        # 5. Подтверждённые живые остатки добиваются, затем ПОВТОРНЫЙ
        # скан (отдельный шаг).
        foreach ($остаток in $потомки) {
            if (-not $остаток.Confirmed) { continue }
            if (-not (Test-ProcessAlive $остаток.Pid)) { continue }

            if ($env:P302_TREE_FAULT -eq 'descendant-remains') {
                $сообщение = 'fault-inject: descendant remains after cleanup (pid=' + $остаток.Pid + ')'
                if ($результат.TreeError) {
                    $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                } else {
                    $результат.TreeError = $сообщение
                }
                Write-Host "  FAULT-INJECT: descendant remains (pid=$($остаток.Pid))"
                continue
            }

            Write-Host "  HARNESS DESCENDANT: pid=$($остаток.Pid) name=$($остаток.Name) — принудительное завершение"
            $выводОстатка = & taskkill /T /F /PID $остаток.Pid 2>&1
            $кодОстатка = $LASTEXITCODE

            if ($кодОстатка -ne 0 -and $кодОстатка -ne 128) {
                $сообщение = "taskkill потомка вернул $кодОстатка (pid=$($остаток.Pid)): $($выводОстатка -join ' | ')"
                if ($результат.TreeError) {
                    $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                } else {
                    $результат.TreeError = $сообщение
                }
            }
        }

        # 6. Повторный скан — итоговая проверка чистоты.
        try {
            $потомки2 = @(Get-ProcessDescendants $пид $старт $результат.StartTimeKnown)
            $результат.TreeConfirmed = $true
            $результат.UnconfirmedPids = @($потомки2 | Where-Object { -not $_.Confirmed } | ForEach-Object { $_.Pid })
            $результат.RemainingPids = @($потомки2 | Where-Object { $_.Confirmed } | Where-Object { Test-ProcessAlive $_.Pid } | ForEach-Object { $_.Pid })
        } catch {
            $сообщение = $_.Exception.Message
            if ($результат.TreeError) {
                $результат.TreeError = $результат.TreeError + '; ' + $сообщение
            } else {
                $результат.TreeError = $сообщение
            }
        }

        if (-not $proc.HasExited) {
            $результат.RemainingPids = @($результат.Pid) + @($результат.RemainingPids)
            $сообщение = "родительский процесс жив после cleanup (pid=$пид)"
            if ($результат.TreeError) {
                $результат.TreeError = $результат.TreeError + '; ' + $сообщение
            } else {
                $результат.TreeError = $сообщение
            }
        }
    }

    # 7. Итог: чисто = без остатков, без неподтверждённых, без ошибок.
    $результат.TreeOk = (
        $результат.TreeConfirmed -and
        $результат.RemainingPids.Count -eq 0 -and
        $результат.UnconfirmedPids.Count -eq 0 -and
        -not $результат.TreeError
    )

    if ($результат.Killed) {
        Write-Host "  HARNESS KILLED (pid=$пид) — happy-path PASS невозможен"
    }

    if (-not $результат.TreeOk) {
        $списокОстатков = if ($результат.RemainingPids.Count -gt 0) { $результат.RemainingPids -join ';' } else { 'нет' }
        Write-Host ("  SAFETY-FAIL: дерево не чистое (pid=$пид): остатки [$списокОстатков]; ошибка: " + $результат.TreeError)
    }

    return $результат
}

# Пост-проверка записи evidence: файл существует, не пуст и содержит
# обязательные маркеры ТЕКУЩЕГО прогона. Успех cmdlet-а записи сам по
# себе не считается доказательством — проверяется результат.
function Test-EvidenceWritten([string]$относительныйПуть, [string[]]$обязательныеМаркеры) {
    try {
        $каталог = 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
        $путь = Join-Path $каталог $относительныйПуть
        if (-not (Test-Path -LiteralPath $путь)) {
            Write-Host "  EVIDENCE VERIFY FAIL: файл отсутствует: $путь"
            return $false
        }
        $файл = Get-Item -LiteralPath $путь
        if ($файл.Length -le 0) {
            Write-Host "  EVIDENCE VERIFY FAIL: файл пуст: $путь"
            return $false
        }
        $содержимое = Get-Content -LiteralPath $путь -Raw
        foreach ($маркер in $обязательныеМаркеры) {
            if (-not $содержимое.Contains($маркер)) {
                Write-Host "  EVIDENCE VERIFY FAIL: нет маркера «$маркер» в $путь"
                return $false
            }
        }
        return $true
    } catch {
        Write-Host "  EVIDENCE VERIFY FAIL: $($_.Exception.Message)"
        return $false
    }
}
