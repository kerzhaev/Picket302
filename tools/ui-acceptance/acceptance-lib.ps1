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

    # В тестовом режиме P302_TASKKILL_FAULT=hang ждать штатного завершения
    # 20 секунд бессмысленно: контракт внутреннего таймаута taskkill
    # проверяется быстро и детерминированно.
    $waitMs = 20000
    if ($env:P302_TASKKILL_FAULT -eq 'hang') { $waitMs = 1000 }

    return Stop-HarnessTree $h.Proc $waitMs
}

# Bounded runner для NATIVE-команды: команда запускается ОТДЕЛЬНЫМ
# процессом и ждётся с явным таймаутом WaitForExit($timeoutMs).
#
# Зачем отдельная функция: параметр $timeoutMs в сигнатуре сам по себе
# НЕ ограничивает время исполнения native-команды. Конструкция
# `& taskkill ...` выполняется СИНХРОННО в потоке вызывающего и может
# висеть бесконечно — реальное ограничение даёт только отдельный
# процесс + WaitForExit($timeoutMs).
#
# Защита от pipe-deadlock: stdout/stderr читаются АСИНХРОННО
# (ReadToEndAsync) с самого старта процесса — буфер трубы никогда не
# переполняется, поэтому дочерний процесс не блокируется на записи.
# Синхронный ReadToEnd() ДО WaitForExit() запрещён: он блокирует поток
# до EOF, а EOF может не наступить (унаследованный дескриптор).
#
# Ограничение сбора вывода — тоже bounded: если потомок удерживает конец
# трубы, чтение отменяется по таймауту, частичный вывод сохраняется.
#
# При таймауте дочерний процесс принудительно завершается (Kill), после
# чего выполняется ТОЛЬКО ограниченное ожидание (бесконечный
# WaitForExit запрещён). Исключение наружу не выходит. При таймауте
# поле Exception ОБЯЗАНО быть непустым и содержать причину (метку,
# pid, таймаут): это заявленная часть результата — потребители
# (Invoke-TaskKillSafe → Stop-HarnessTree → TreeError) читают именно
# Exception, а не только Диагностика.
function Invoke-BoundedCommand {
    param(
        [string]$FilePath,
        [string]$Arguments = '',
        [int]$TimeoutMs = 15000,
        [string]$label = 'bounded',
        [int]$ReadTimeoutMs = 5000
    )

    $результат = @{
        Attempted = $false
        Started = $false
        ChildPid = $null
        ExitCode = $null
        StdOut = @()
        StdErr = @()
        Output = @()
        TimedOut = $false
        Killed = $false
        Exception = $null
        CleanupError = $null
        WaitMs = 0
        ElapsedMs = 0
        Диагностика = ''
    }

    if ([string]::IsNullOrWhiteSpace($FilePath)) {
        $результат.Exception = 'не указан исполняемый файл'
        return $результат
    }

    if ($TimeoutMs -lt 1) { $TimeoutMs = 1 }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    # stdin не наследуется: дочерний процесс не должен удерживать
    # stdin вызывающего (иначе зависание на чтении ввода).
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $child = $null
    $outTask = $null
    $errTask = $null
    $clock = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $child = [System.Diagnostics.Process]::Start($psi)
        $результат.Started = $true
        $результат.Attempted = $true
        $результат.ChildPid = $child.Id

        try { $child.StandardInput.Close() } catch { }

        # Асинхронное чтение с самого старта — трубы не переполняются,
        # pipe-deadlock невозможен.
        $outTask = $child.StandardOutput.ReadToEndAsync()
        $errTask = $child.StandardError.ReadToEndAsync()

        $waitClock = [System.Diagnostics.Stopwatch]::StartNew()
        $завершился = $child.WaitForExit($TimeoutMs)
        $waitClock.Stop()
        $результат.WaitMs = $waitClock.ElapsedMilliseconds

        if (-not $завершился) {
            $результат.TimedOut = $true
            $результат.Диагностика = "команда не завершилась за $TimeoutMs мс (метка=$label, pid=$($child.Id))"
            # Exception обязателен при таймауте: это заявленное поле
            # результата, а не только Диагностика. Invoke-TaskKillSafe
            # копирует Exception наружу — нулевая диагностика timeout
            # терялась бы потребителями.
            $результат.Exception = $результат.Диагностика
            Write-Host "  BOUNDED-RUN TIMEOUT: $($результат.Диагностика)"

            try {
                $child.Kill()
                $результат.Killed = $true
            } catch {
                # Причину таймаута НЕ затираем: ошибка Kill добавляется
                # к существующему сообщению.
                $результат.Exception = ($результат.Exception + '; Kill не выполнен: ' + $_.Exception.Message)
            }

            # После Kill — ТОЛЬКО ограниченное ожидание.
            try { [void]$child.WaitForExit(2000) } catch { }
        } else {
            try {
                $результат.ExitCode = $child.ExitCode
            } catch {
                $результат.Диагностика = "ExitCode недоступен (метка=$label): $($_.Exception.Message)"
            }
        }

        # Сбор вывода — bounded (иначе потомок, удерживающий конец трубы,
        # заблокировал бы сбор навсегда).
        $budget = if ($результат.TimedOut) { 2000 } else { $ReadTimeoutMs }
        $outText = ''
        $errText = ''

        if ($outTask -ne $null) {
            try {
                if ($outTask.Wait($budget)) {
                    $outText = [string]$outTask.Result
                } else {
                    $результат.Диагностика = ($результат.Диагностика + '; stdout не собран (таймаут чтения)').TrimStart(';')
                }
            } catch {
                $результат.Диагностика = ($результат.Диагностика + "; stdout: $($_.Exception.Message)").TrimStart(';')
            }
        }

        if ($errTask -ne $null) {
            try {
                if ($errTask.Wait($budget)) {
                    $errText = [string]$errTask.Result
                } else {
                    $результат.Диагностика = ($результат.Диагностика + '; stderr не собран (таймаут чтения)').TrimStart(';')
                }
            } catch {
                $результат.Диагностика = ($результат.Диагностика + "; stderr: $($_.Exception.Message)").TrimStart(';')
            }
        }

        $результат.StdOut = @($outText -split "`r?`n" | Where-Object { $_ -ne '' })
        $результат.StdErr = @($errText -split "`r?`n" | Where-Object { $_ -ne '' })
        $результат.Output = @($результат.StdOut) + @($результат.StdErr)
    } catch {
        $результат.Exception = $_.Exception.Message
        $результат.Attempted = $true
    } finally {
        $clock.Stop()
        $результат.ElapsedMs = $clock.ElapsedMilliseconds

        if ($child -ne $null) {
            try { $child.Dispose() } catch {
                if ($результат.CleanupError) {
                    $результат.CleanupError = $результат.CleanupError + "; dispose: $($_.Exception.Message)"
                } else {
                    $результат.CleanupError = "dispose: $($_.Exception.Message)"
                }
            }
        }
    }

    return $результат
}

# Бюджет ожидания taskkill. В тестовом режиме P302_TASKKILL_FAULT=hang
# используется МАЛЫЙ бюджет: контракт внутреннего таймаута должен
# проверяться быстро и детерминированно, а не «подождать 15 секунд».
function Get-TaskKillTimeoutMs([int]$поУмолчанию = 15000) {
    if ($env:P302_TASKKILL_FAULT -eq 'hang') { return 800 }
    if ($поУмолчанию -lt 1) { return 1 }
    return $поУмолчанию
}

# Безопасный вызов taskkill /T /F /PID: гонка «процесс исчез между
# scan и taskkill» НЕ превращается в необработанное исключение, а
# ВРЕМЯ ИСПОЛНЕНИЯ САМОЙ taskkill ограничено $timeoutMs.
#
# Причина исходного дефекта: PowerShell 5.1 при
# $ErrorActionPreference='Stop' переводит stderr native-команды в
# terminating error. taskkill пишет в stderr «ERROR: The process "N"
# not found.» именно в гонке, когда процесс уже завершился — и
# исключение прерывало Stop-HarnessTree ДО финального post-scan
# (терялись RemainingPids/UnconfirmedPids и структурированный
# результат).
#
# Второй (текущий) дефект: синхронный `& taskkill ...` не имеет
# собственного таймаута — зависшая taskkill висела бы бесконечно.
# Поэтому taskkill запускается ОТДЕЛЬНЫМ процессом через
# Invoke-BoundedCommand с WaitForExit($timeoutMs).
#
# Контракт helper-а:
#   - native-команда — отдельный процесс с bounded wait;
#   - stdout/stderr читаются асинхронно (pipe-deadlock невозможен);
#   - по таймауту процесс принудительно завершается, ожидание после
#     Kill — тоже ограниченное;
#   - результат структурирован: Attempted/Pid/Operation/ExitCode/
#     Output/Exception/NotFound/AlreadyExited/TimedOut/Success;
#   - not-found (код 128 или «not found» в выводе) — это признак
#     benign-race, а не успех: окончательную классификацию даёт
#     финальный post-scan вызывающего кода;
#   - таймаут — это ошибка (Success=false), а не not-found.
function Invoke-TaskKillSafe([int]$идентификатор, [string]$метка, [int]$timeoutMs = 15000, [bool]$сДеревом = $true) {
    $результат = @{
        Attempted = $false
        Pid = $идентификатор
        Operation = $метка
        ExitCode = $null
        Output = @()
        StdOut = @()
        StdErr = @()
        Exception = $null
        NotFound = $false
        AlreadyExited = $false
        TimedOut = $false
        Success = $false
        CleanupError = $null
        WaitMs = 0
        ElapsedMs = 0
        Runner = 'taskkill'
        ChildPid = $null
    }

    if ($идентификатор -le 0) {
        $результат.Exception = 'недопустимый PID'
        return $результат
    }

    # --- Определение runner-а ---------------------------------------------
    # Обычный случай — СИСТЕМНЫЙ taskkill отдельным процессом.
    # Флаги передаются ОТДЕЛЬНЫМИ аргументами: одной строкой taskkill их
    # не понимает. Без /T родитель завершается один, а потомок остаётся
    # живым (нужно для проверки гонки на стадии очистки потомков).
    $runner = "$env:SystemRoot\System32\taskkill.exe"
    $аргументы = if ($сДеревом) { "/T /F /PID $идентификатор" } else { "/F /PID $идентификатор" }
    $результат.Runner = 'taskkill'

    # Тестовый seam P302_TASKKILL_FAULT=hang: системный taskkill
    # подвешивать НЕ нужно — подменяется runner на синтетический
    # процесс, который спит дольше таймаута. Контракт внутреннего
    # таймаута проверяется детерминированно.
    if ($env:P302_TASKKILL_FAULT -eq 'hang') {
        $runner = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        if (-not (Test-Path -LiteralPath $runner)) { $runner = 'powershell.exe' }
        $аргументы = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Start-Sleep -Seconds 300"'
        $результат.Runner = 'fault-hang-sleeper'
    }

    # Реальный bounded runner: отдельный процесс + WaitForExit($timeoutMs).
    $запуск = Invoke-BoundedCommand -FilePath $runner -Arguments $аргументы -TimeoutMs $timeoutMs -Label $метка

    $результат.Attempted = $запуск.Attempted
    $результат.ExitCode = $запуск.ExitCode
    $результат.StdOut = @($запуск.StdOut)
    $результат.StdErr = @($запуск.StdErr)
    $результат.Output = @($запуск.Output)
    $результат.TimedOut = $запуск.TimedOut
    $результат.WaitMs = $запуск.WaitMs
    $результат.ElapsedMs = $запуск.ElapsedMs
    $результат.CleanupError = $запуск.CleanupError
    $результат.Exception = $запуск.Exception
    $результат.ChildPid = $запуск.ChildPid

    if ($результат.TimedOut) {
        # Таймаут — это ошибка, а НЕ успех и НЕ not-found.
        # ExitCode остаётся null: процесс не завершился штатно.
        $результат.Success = $false
        Write-Host "  TASKKILL TIMEOUT: taskkill не завершился за $timeoutMs мс (pid=$идентификатор, метка=$метка, runner=$($результат.Runner))"
        return $результат
    }

    if ($результат.Exception) {
        $результат.Success = $false
        return $результат
    }

    # 128 = «процесс не найден» (taskkill); текст «not found» —
    # тот же случай на другой локали.
    $текст = ($результат.Output -join ' | ')
    if ($результат.ExitCode -eq 128 -or $текст -match 'not found' -or $текст -match 'не найден') {
        $результат.NotFound = $true
        $результат.AlreadyExited = $true
    }

    # Успех = код 0. Not-found — НЕ успех: это гонка, окончательное
    # решение принимает post-scan вызывающего кода.
    $результат.Success = ($результат.ExitCode -eq 0)

    return $результат
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
        TaskKillExitCode = $null
        HarnessNotExited = $false
        StartTime = $null
        DescendantKillResults = @()
        DescendantKillErrors = @()
        AlreadyExitedPids = @()
        TaskKillRacePids = @()
        TaskKillTimedOutPids = @()
    }
}

# Ограниченное ожидание завершения процесса — ЕДИНСТВЕННЫЙ допустимый
# способ ожидать процесс в acceptance-драйверах: бесконечные
# бесконечные вызовы WaitForExit без таймаута запрещены (процесс с ошибкой teardown может
# остаться живым — драйвер завис бы вместо exit 1). Возвращает
# структурированный результат: Exited / ExitCode (null, если процесс не
# завершился или код недоступен из-за уничтоженного процесса) / TimedOut /
# StartTime (для диагностики PID reuse) / Диагностика.
function Wait-ProcessExitBounded($proc, [int]$timeoutMs, [string]$метка) {
    $результат = @{ Exited = $false; ExitCode = $null; TimedOut = $false; StartTime = $null; Диагностика = '' }

    if ($proc -eq $null) {
        $результат.Диагностика = "процесс null (метка=$метка)"
        return $результат
    }

    try { $результат.StartTime = $proc.StartTime } catch {
        # StartTime недоступен (процесс уже уничтожен) — не критично.
    }

    try {
        if ($proc.WaitForExit($timeoutMs)) {
            $результат.Exited = $true

            try {
                $результат.ExitCode = $proc.ExitCode
            } catch {
                # Уже уничтоженный процесс: ExitCode может быть недоступен —
                # это не превращается в исключение наружу.
                $результат.Диагностика = "ExitCode недоступен (метка=$метка): $($_.Exception.Message)"
            }
        } else {
            $результат.TimedOut = $true
            $пид = '?'
            try { $пид = $proc.Id } catch { }
            $результат.Диагностика = "процесс не завершился за $timeoutMs мс (метка=$метка, pid=$пид, старт=$($результат.StartTime))"
            Write-Host "  BOUNDED-WAIT TIMEOUT: $($результат.Диагностика)"
        }
    } catch {
        $результат.Диагностика = "ожидание не выполнено (метка=$метка): $($_.Exception.Message)"
        Write-Host "  BOUNDED-WAIT: $($результат.Диагностика)"
    }

    return $результат
}

# Единая ограниченная остановка harness для драйверов (emptybook/printflow):
# - null/неполный результат/исключение остановки — fail-флаги, без
#   бесконечных ожиданий;
# - TreeOk=false — немедленный tree-cleanup-failed, затем МАКСИМУМ ОДНА
#   ограниченная повторная попытка аварийного teardown и повторный
#   bounded post-scan (отдельный шаг);
# - живые/неподтверждённые PID после post-scan — в диагностику;
# - процесс НЕ ждётся бесконечно ни в одной ветви.
function Invoke-HarnessStop($h, [string]$сценарий, [int]$retryWaitMs = 5000) {
    $флаги = [System.Collections.Generic.List[string]]::new()
    $результат = $null
    $повтор = $null
    $пост = $null

    if ($h -eq $null -or $h.Proc -eq $null) {
        $флаги.Add('teardown-failed')
        Write-Host "  SAFETY-FAIL: harness недоступен для остановки (сценарий=$сценарий, pid=нет)"
        return @{ Результат = $null; Повтор = $null; ПостПроверка = $null; Флаги = $флаги }
    }

    try {
        $результат = Stop-Acceptance $h
    } catch {
        $флаги.Add('stop-exception')
        Write-Host "  SAFETY-FAIL: исключение остановки harness (сценарий=$сценарий): $($_.Exception.Message)"
    }

    if ($результат -eq $null -or -not ($результат -is [hashtable]) -or -not $результат.ContainsKey('TreeOk')) {
        $флаги.Add('stop-result-incomplete')
        Write-Host "  SAFETY-FAIL: результат остановки отсутствует или неполный (сценарий=$сценарий)"
        return @{ Результат = $результат; Повтор = $null; ПостПроверка = $null; Флаги = $флаги }
    }

    if ($результат.Pid -gt 0) {
        $пидДляЖурнала = $результат.Pid
        Write-Host "  Остановка harness (pid=$пидДляЖурнала): graceful=$($результат.Graceful) killed=$($результат.Killed) TreeOk=$($результат.TreeOk)"
    }

    if (-not $результат.TreeOk) {
        # Немедленный fail-флаг; без блокировки на обычном WaitForExit.
        $флаги.Add('tree-cleanup-failed')
        Write-Host ("  SAFETY-FAIL: дерево процессов не чистое после остановки (pid=" + $результат.Pid + "): " + $результат.TreeError)

        foreach ($живой in $результат.RemainingPids) {
            $флаги.Add('remaining-descendants')
            Write-Host "  SAFETY-FAIL: живой PID после остановки: $живой"
        }

        foreach ($неподтверждённый in $результат.UnconfirmedPids) {
            $флаги.Add('unconfirmed-descendants')
            Write-Host "  SAFETY-FAIL: неподтверждённый PID (PID reuse риск): $неподтверждённый"
        }

        # Максимум ОДНА ограниченная попытка аварийного teardown.
        try {
            $повтор = Stop-HarnessTree $h.Proc $retryWaitMs
        } catch {
            $флаги.Add('stop-exception')
            Write-Host "  SAFETY-FAIL: повторная попытка аварийного teardown: $($_.Exception.Message)"
        }

        if ($повтор -ne $null -and ($повтор -is [hashtable])) {
            if ($повтор.TreeError) {
                Write-Host ("  Повторная остановка (pid=" + $повтор.Pid + "): TreeOk=" + $повтор.TreeOk + "; ошибка: " + $повтор.TreeError)
            }

            if (-not $повтор.TreeConfirmed) {
                $флаги.Add('tree-verification-failed')
            }

            if ($повтор.RemainingPids.Count -gt 0 -and $повтор.TreeOk) {
                foreach ($живой in $повтор.RemainingPids) {
                    $флаги.Add('remaining-descendants')
                    Write-Host "  SAFETY-FAIL: живой PID после повторной остановки: $живой"
                }
            }
        }

        # Повторный bounded post-scan — отдельный шаг после завершения.
        try {
            $пост = Test-ProcessTreeGone $h.Proc
        } catch {
            $флаги.Add('tree-verification-failed')
            Write-Host "  SAFETY-FAIL: пост-скан дерева процессов: $($_.Exception.Message)"
        }

        if ($пост -ne $null) {
            if (-not $пост.Ok) {
                $флаги.Add('tree-postcheck-failed')
                Write-Host ("  SAFETY-FAIL: пост-скан дерева (pid=" + $пост.Pid + "): остатки [" +
                    ($пост.RemainingPids -join ';') + "] неподтверждённые [" +
                    ($пост.UnconfirmedPids -join ';') + "] ошибка: " + $пост.Error)
            }

            foreach ($живой in $пост.RemainingPids) {
                if ($флаги -notcontains 'remaining-descendants') { $флаги.Add('remaining-descendants') }
            }

            foreach ($неподтверждённый in $пост.UnconfirmedPids) {
                if ($флаги -notcontains 'unconfirmed-descendants') { $флаги.Add('unconfirmed-descendants') }
            }
        }
    }

    return @{ Результат = $результат; Повтор = $повтор; ПостПроверка = $пост; Флаги = $флаги }
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
# Каталог для evidence-файлов. По умолчанию — каталог документации
# репозитория; переменная P302_EVIDENCE_ROOT перенаправляет запись в
# произвольный каталог (используется driver-level тестами, чтобы
# fault-прогоны НЕ загрязняли отслеживаемые файлы репозитория).
function Get-EvidenceRoot {
    if ($env:P302_EVIDENCE_ROOT) { return $env:P302_EVIDENCE_ROOT }
    return 'Z:\Picket302\docs\ux-audit-2026-11-03\после-этапа-редизайна'
}

function ЗаписатьEvidence([string]$относительныйПуть, [string]$заголовок, [string]$режимСтрока, $журнал) {
    try {
        $каталог = Get-EvidenceRoot
        $строки = @(
            "# $заголовок",
            '',
            "Прогон: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), профиль: временный (guid), $режимСтрока.",
            ''
        ) + @($журнал)

        if (-not (Test-Path -LiteralPath $каталог)) {
            New-Item -ItemType Directory -Force -Path $каталог -ErrorAction Stop | Out-Null
        }

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
        # CreationDate возвращается: post-scan сверяет его с StartTime
        # живого процесса, чтобы отличить «наш потомок» от нового
        # процесса, занявшего тот же PID (PID reuse).
        $найденные += @{
            Pid = [int]$запись.ProcessId
            Name = $запись.Name
            Confirmed = $подтверждён
            CreationDate = $запись.CreationDate
        }

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
    # PID 0 — это «System Idle Process» в Windows: Get-Process -Id 0
    # УСПЕШНО возвращает процесс. Невалидный PID (0 или отрицательный)
    # не может считаться живым — иначе null-подстановка давала бы
    # ложный «жив» и ломала fail-closed классификацию.
    if ($идентификатор -le 0) { return $false }

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
        $результат.StartTime = $старт
    } catch {
        $старт = [datetime]::MinValue
        $результат.StartTimeKnown = $false
        Write-Host "  TREE: StartTime недоступен заранее (pid=$пид) — идентичность потомков не подтвердить, проверка fail-closed"
    }

    # Тестовый fault (только driver-level liveness-тесты): родитель
    # логически остаётся живым, cleanup НЕ подтверждён; реальные процессы
    # не убиваются — внешняя уборка за fault-тестом (launcher).
    if ($env:P302_DRIVER_FAULT -eq 'parent-alive-after-stop') {
        $результат.TreeError = 'fault-inject: parent alive after stop'
        $результат.RemainingPids = @($пид)
        $результат.TreeOk = $false
        $результат.TreeConfirmed = $true
        $результат.HarnessNotExited = $true
        Write-Host "  FAULT-INJECT: parent alive after stop (pid=$пид)"
        return $результат
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
                # Безопасный helper: гонка «процесс исчез» не бросает
                # исключение, taskkill имеет bounded wait.
                #
                # Для race-швов родитель завершается БЕЗ /T: иначе
                # taskkill /T убьёт всё дерево, потомок не доживёт до
                # стадии очистки, и гонка «исчез между scan и taskkill»
                # не будет проверена вовсе.
                $родительБезДерева = (
                    $env:P302_TREE_FAULT -eq 'descendant-disappears-before-taskkill' -or
                    $env:P302_TREE_FAULT -eq 'descendant-not-found-but-alive')

                $killParent = Invoke-TaskKillSafe $пид 'parent' (Get-TaskKillTimeoutMs 15000) (-not $родительБезДерева)
                $кодTaskkill = $killParent.ExitCode
                $выводForce = @($killParent.Output)

                $результат.KillOutput = @($выводForce)
                $результат.TaskKillExitCode = $кодTaskkill

                if ($killParent.TimedOut) {
                    $результат.TaskKillTimedOutPids += $пид
                    $сообщение = "taskkill родителя не завершился за таймаут (pid=$пид)"
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    Write-Host "  SAFETY-FAIL: $сообщение"
                }

                if ($killParent.Exception) {
                    $сообщение = "taskkill родителя: $($killParent.Exception) (pid=$пид)"
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    Write-Host "  SAFETY-FAIL: $сообщение"
                }

                if ($killParent.CleanupError) {
                    $сообщение = "taskkill родителя: ошибка уборки ресурсов runner-а (pid=$пид): $($killParent.CleanupError)"
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    Write-Host "  SAFETY-FAIL: $сообщение"
                }

                if ($killParent.NotFound) {
                    # Гонка: родитель исчез между проверкой и taskkill.
                    # Если PID действительно мёртв — benign race (отметка в
                    # диагностике, TreeError НЕ ставится); если PID жив —
                    # fail-closed (решение подтверждает post-scan).
                    if (-not (Test-ProcessAlive $пид)) {
                        $результат.TaskKillRacePids += $пид
                        $результат.AlreadyExitedPids += $пид
                        Write-Host "  TREE RACE: родитель завершился сам (pid=$пид) — benign race"
                    } else {
                        $сообщение = "taskkill: процесс не найден, но PID жив (pid=$пид) — возможен PID reuse"
                        if ($результат.TreeError) {
                            $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                        } else {
                            $результат.TreeError = $сообщение
                        }
                        Write-Host "  SAFETY-FAIL: $сообщение"
                    }
                } elseif (-not $killParent.Success -and -not $killParent.TimedOut -and -not $killParent.Exception) {
                    # Ненулевой код без признака not-found — реальная ошибка.
                    $сообщение = "taskkill вернул $кодTaskkill (pid=$пид): $($выводForce -join ' | ')"
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    Write-Host "  SAFETY-FAIL: $сообщение"
                }

                try { $proc.Kill() } catch {
                    if (-not $proc.HasExited) {
                        Write-Host "  HARNESS KILL: Kill не выполнен: $($_.Exception.Message)"
                    }
                }
                # Ожидание после Kill — ТОЛЬКО ограниченное (бесконечный
                # WaitForExit запрещён).
                $ожиданиеKilled = Wait-ProcessExitBounded $proc 10000 'stop-harness-tree-parent'
                if ($ожиданиеKilled.TimedOut) {
                    $результат.HarnessNotExited = $true
                    $сообщение = "процесс не завершился после Kill за 10000 мс (pid=$пид)"
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    Write-Host "  SAFETY-FAIL: $сообщение"
                }
            }

            if (-not $результат.KillOutput) {
                $результат.KillOutput = @($выводForce)
            }

            if ($null -eq $результат.TaskKillExitCode) {
                $результат.TaskKillExitCode = $кодTaskkill
            }

            $результат.Killed = $true

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

            $выводОстатка = @()
            $кодОстатка = $null

            if ($env:P302_TREE_FAULT -eq 'descendant-not-found-but-alive') {
                # Детерминированная имитация гонки, где процесс НЕ исчезает:
                # taskkill сообщает not-found, но PID жив → post-scan
                # обязан дать RemainingPids и TreeOk=false.
                $выводОстатка = @("ERROR: The process `"$($остаток.Pid)`" not found.")
                $кодОстатка = 128
                $результат.DescendantKillResults += @{
                    Attempted = $true; Pid = $остаток.Pid; Operation = 'descendant-fault-not-found'
                    ExitCode = 128; Output = @($выводОстатка); StdOut = @(); StdErr = @()
                    Exception = $null
                    NotFound = $true; AlreadyExited = $true; TimedOut = $false; Success = $false
                    CleanupError = $null; WaitMs = 0; ElapsedMs = 0; Runner = 'fault-not-found'
                }
            } else {
                if ($env:P302_TREE_FAULT -eq 'descendant-disappears-before-taskkill') {
                    # Детерминированная имитация ЕСТЕСТВЕННОЙ гонки: потомок
                    # исчезает САМ между scan и taskkill (завершаем его
                    # здесь), после чего taskkill честно возвращает
                    # not-found. Гонка воспроизводится без случайного окна.
                    $преждевременный = Invoke-TaskKillSafe $остаток.Pid 'descendant-race-prerace' (Get-TaskKillTimeoutMs 15000)
                    $результат.DescendantKillResults += $преждевременный
                    Write-Host "  TREE RACE (fault): descendant исчез до taskkill (pid=$($остаток.Pid))"
                }

                # Безопасный helper: «процесс исчез» → not-found без
                # исключения; bounded wait; структурированный результат.
                $killDescendant = Invoke-TaskKillSafe $остаток.Pid 'descendant' (Get-TaskKillTimeoutMs 15000)
                $выводОстатка = @($killDescendant.Output)
                $кодОстатка = $killDescendant.ExitCode
                $результат.DescendantKillResults += $killDescendant

                if ($killDescendant.TimedOut) {
                    $результат.TaskKillTimedOutPids += $остаток.Pid
                    $сообщение = "taskkill потомка не завершился за таймаут (pid=$($остаток.Pid))"
                    $результат.DescendantKillErrors += $сообщение
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    continue
                }

                if ($killDescendant.Exception) {
                    $сообщение = "taskkill потомка: $($killDescendant.Exception) (pid=$($остаток.Pid))"
                    $результат.DescendantKillErrors += $сообщение
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                    continue
                }

                if ($killDescendant.CleanupError) {
                    $сообщение = "taskkill потомка: ошибка уборки ресурсов runner-а (pid=$($остаток.Pid)): $($killDescendant.CleanupError)"
                    $результат.DescendantKillErrors += $сообщение
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                }

                if ($killDescendant.NotFound) {
                    if (-not (Test-ProcessAlive $остаток.Pid)) {
                        # Benign race: потомок исчез между scan и taskkill.
                        # Отметка остаётся в диагностике; окончательную
                        # классификацию даёт post-scan.
                        $результат.TaskKillRacePids += $остаток.Pid
                        $результат.AlreadyExitedPids += $остаток.Pid
                        Write-Host "  TREE RACE: потомок уже завершён (pid=$($остаток.Pid)) — benign race"
                    } else {
                        # not-found, но PID жив → fail-closed (решение
                        # подтверждает post-scan: RemainingPids).
                        $сообщение = "taskkill потомка: процесс не найден, но PID жив (pid=$($остаток.Pid)) — возможен PID reuse"
                        $результат.DescendantKillErrors += $сообщение
                        if ($результат.TreeError) {
                            $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                        } else {
                            $результат.TreeError = $сообщение
                        }
                    }
                    continue
                }

                if (-not $killDescendant.Success) {
                    $сообщение = "taskkill потомка вернул $кодОстатка (pid=$($остаток.Pid)): $($выводОстатка -join ' | ')"
                    $результат.DescendantKillErrors += $сообщение
                    if ($результат.TreeError) {
                        $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                    } else {
                        $результат.TreeError = $сообщение
                    }
                }
            }
        }

        # 6. Повторный скан — итоговая проверка чистоты (ВСЕГДА
        # выполняется: отдельный шаг после очистки потомков).
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

        # 6b. ПРЯМАЯ проверка каждого PID потомка первого скана: после
        # смерти родителя потомки переподчиняются, и повторный скан по
        # ParentProcessId может их не найти — «дерево чисто» тогда
        # означало бы «процесс не найден», а не «процесс завершён».
        # Идентичность (PID reuse) проверяется по времени старта.
        foreach ($остаток in $потомки) {
            if (-not $остаток.Confirmed) { continue }
            if (-not (Test-ProcessAlive $остаток.Pid)) { continue }

            # Причина в TreeError ставится для КАЖДОГО живого подтверждённого
            # потомка — даже если rescan уже перечислил его в RemainingPids:
            # иначе «остался живым» осталось бы без объяснения.
            $подтверждён = $true

            try {
                $живой = Get-Process -Id $остаток.Pid -ErrorAction Stop

                if ($null -ne $остаток.CreationDate) {
                    $расхождение = [Math]::Abs(($живой.StartTime - $остаток.CreationDate).TotalSeconds)

                    if ($расхождение -gt 2) {
                        # Другой процесс занял PID после завершения
                        # потомка — убивать его нельзя (fail-closed).
                        $подтверждён = $false
                    }
                }
            } catch {
                $подтверждён = $true
            }

            if (-not $подтверждён) {
                if ($результат.UnconfirmedPids -notcontains $остаток.Pid) {
                    $результат.UnconfirmedPids += $остаток.Pid
                }

                $сообщение = "потомок с неподтверждённой идентичностью (PID reuse), pid=$($остаток.Pid)"
                if ($результат.TreeError) {
                    $результат.TreeError = $результат.TreeError + '; ' + $сообщение
                } else {
                    $результат.TreeError = $сообщение
                }

                continue
            }

            if ($результат.RemainingPids -notcontains $остаток.Pid) {
                $результат.RemainingPids += $остаток.Pid
            }

            $сообщение = "потомок остался жив после cleanup (pid=$($остаток.Pid))"
            if ($результат.TreeError) {
                $результат.TreeError = $результат.TreeError + '; ' + $сообщение
            } else {
                $результат.TreeError = $сообщение
            }
        }

        if (-not $proc.HasExited) {
            $результат.HarnessNotExited = $true
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
        $каталог = Get-EvidenceRoot
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
