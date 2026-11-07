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

# Штатное завершение: CLOSE -> ожидание 20 c -> Kill при необходимости.
# Возвращает $true, если пришлось убивать процесс (для драйвера это FAIL).
function Stop-Acceptance($h) {
    if ($h -eq $null -or $h.Proc -eq $null) { return $false }
    if ($h.Proc.HasExited) { return $false }
    try {
        $h.Proc.StandardInput.WriteLine('CLOSE')
    } catch {
        # Неудачная доставка CLOSE означает, что harness уже завершается:
        # сам по себе это не провал прогона — код возврата проверяется ниже.
        Write-Host "  CLOSE не доставлен (harness завершается): $($_.Exception.Message)"
    }
    if (-not $h.Proc.WaitForExit(20000)) {
        $h.Proc.Kill()
        $h.Proc.WaitForExit()
        Write-Host '  HARNESS KILLED (штатное завершение не сработало) — сценарий фиксируется FAIL'
        return $true
    }
    return $false
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

function Exit-SharedTestLock($мьютекс) {
    if ($мьютекс -eq $null) { return }
    try {
        $мьютекс.ReleaseMutex()
    } catch {
        Write-Host ("  LOCK: ReleaseMutex не выполнен: " + $_.Exception.Message)
    }
    $мьютекс.Dispose()
}

# Аварийное завершение harness с деревом процессов: CLOSE -> Kill ->
# taskkill /T /F + проверка дочерних процессов. Возвращает $true, если
# пришлось убивать (драйвер обязан выставить fail-флаг — happy-path PASS
# невозможен).
function Stop-HarnessTree($h) {
    if ($h -eq $null -or $h.Proc -eq $null) { return $false }
    if ($h.Proc.HasExited) { return $false }
    $pidHarness = $h.Proc.Id
    try {
        $h.Proc.StandardInput.WriteLine('CLOSE')
    } catch {
        Write-Host "  CLOSE не доставлен (harness завершается): $($_.Exception.Message)"
    }
    if (-not $h.Proc.WaitForExit(20000)) {
        Write-Host "  HARNESS KILL: штатное завершение не сработало (pid=$pidHarness) — taskkill /T /F"
        & taskkill /T /F /PID $pidHarness | Out-Null
        try { $h.Proc.Kill() } catch {
            Write-Host "  HARNESS KILL: Kill не выполнен: $($_.Exception.Message)"
        }
        try { $h.Proc.WaitForExit(10000) | Out-Null } catch {}

        if (-not $h.Proc.HasExited) {
            Write-Host "  SAFETY-FAIL: harness не завершён после Kill (pid=$pidHarness)"
        }

        # Дочерние процессы дерева: проверка и принудительное завершение.
        $дети = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$pidHarness" -ErrorAction SilentlyContinue)
        foreach ($дочерний in $дети) {
            Write-Host "  HARNESS CHILD: pid=$($дочерний.ProcessId) name=$($дочерний.Name) — принудительное завершение"
            & taskkill /T /F /PID $дочерний.ProcessId | Out-Null
        }

        Write-Host "  HARNESS KILLED (pid=$pidHarness) — happy-path PASS невозможен"
        return $true
    }
    return $false
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
