# Общая библиотека acceptance-драйвера Picket302 (fail-closed):
# - запуск harness с явным профилем; фоновое чтение stdout;
# - ожидание окон по заголовку; BM_CLICK кнопок;
# - отзывчивость UI через SendMessageTimeout WM_NULL;
# - fingerprint реального профиля через [Environment]::GetFolderPath
#   (переменная APPDATA не используется — она может отличаться от
#   фактического пути Known Folder).
$ErrorActionPreference = 'Continue'

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
    $map = @{}
    if (-not (Test-Path $dir)) { return $map }
    $хеш = [System.Security.Cryptography.SHA256]::Create()
    try {
        Get-ChildItem -Recurse -File $dir -ErrorAction SilentlyContinue | ForEach-Object {
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
    try { $h.Proc.StandardInput.WriteLine('CLOSE') } catch {}
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

# Пишет evidence-файл прогона; возвращает $true при успешной записи.
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
