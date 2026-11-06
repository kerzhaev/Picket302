# Общая библиотека acceptance-драйвера Picket302:
# - запуск harness с явным профилем и гардом изоляции;
# - ожидание окон по заголовку внутри процесса;
# - нажатие кнопок модальных окон (BM_CLICK, без UIA — устойчиво);
# - проверка отзывчивости UI-потока (SendMessageTimeout WM_NULL);
# - sentinel-файл и отпечатки реального профиля до/после.
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
    public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

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

Add-Type -AssemblyName System.Security

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

    $ready = $null
    for ($i = 0; $i -lt 60; $i++) {
        $line = $p.StandardOutput.ReadLine()
        if ($line -eq $null) { break }
        Write-Host "  harness: $line"
        if ($line.StartsWith('READY')) { $ready = $line; break }
    }
    if ($ready -eq $null) {
        $err = $p.StandardError.ReadToEnd()
        Write-Host "  READY NOT RECEIVED; stderr: $err"
        return $null
    }
    $hwnd = [IntPtr][long](($ready -split ' ')[1])
    return @{ Proc = $p; Hwnd = $hwnd; Ready = $ready }
}

function Stop-Acceptance($h) {
    if ($h -eq $null) { return }
    try { $h.Proc.StandardInput.WriteLine('CLOSE') } catch {}
    if (-not $h.Proc.WaitForExit(20000)) {
        Write-Host '  harness did not exit - killing (штатная уборка не удалась)'
        $h.Proc.Kill()
    }
    $h.Proc.WaitForExit()
}

function Wait-AppWindow([uint32]$procId, [string]$title, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $h = [AccWin]::FindByTitle($procId, $title)
        if ($h -ne [IntPtr]::Zero) { return $h }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

function Show-Evidence([string]$phase, [IntPtr]$hwnd) {
    $alive = $hwnd -ne [IntPtr]::Zero
    $title = if ($alive) { [AccWin]::Title($hwnd) } else { '<not found>' }
    Write-Host ("  [{0}] hwnd={1} title='{2}' utc={3}" -f $phase, $hwnd, $title, (Get-Date).ToUniversalTime().ToString('o'))
}

function Test-ProfileFingerprint([string]$dir) {
    $map = @{}
    if (-not (Test-Path $dir)) { return $map }
    Get-ChildItem -Recurse -File $dir -ErrorAction SilentlyContinue | ForEach-Object {
        $хеш = [System.Security.Cryptography.SHA256]::Create()
        try {
            $байты = $хеш.ComputeHash([System.IO.File]::ReadAllBytes($_.FullName))
            $sha = [System.BitConverter]::ToString($байты).Replace('-', '')
        } finally { $хеш.Dispose() }
        $map[$_.FullName] = "$sha|$($_.LastWriteTimeUtc.Ticks)"
    }
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
    } finally { $хеш.Dispose() }
    return "$sha|$( (Get-Item $path).LastWriteTimeUtc.Ticks )"
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

# Ждёт окно заголовка $title, различая рабочий диалог (есть «Отмена») и
# информационный MessageBox (только «ОК»): MessageBox подтверждается и
# ожидание продолжается. Возвращает hwnd диалога.
function Wait-DialogOrConfirm([uint32]$procId, [string]$title, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = [AccWin]::FindByTitle($procId, $title)
        if ($w -ne [IntPtr]::Zero) {
            $отмена = [AccWin]::FindButton($w, 'Отмена')
            if ($отмена -ne [IntPtr]::Zero) { return $w }
            $ок = [AccWin]::FindButton($w, 'ОК')
            if ($ок -ne [IntPtr]::Zero) {
                Write-Host ("  confirm-box: '" + [AccWin]::Title($w) + "' -> ОК")
                [AccWin]::Click($ок)
                Start-Sleep -Milliseconds 600
                $deadline = (Get-Date).AddSeconds($seconds)
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

# Закрывает случайные MessageBox процесса (класс #32770), снимая каждый
# в диагностику; возвращает количество закрытых.
function Close-MessageBoxes([uint32]$procId, [string]$диагностика) {
    $закрыто = 0
    foreach ($w in [AccWin]::WindowsOf($procId)) {
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($w, $cls, 256) | Out-Null
        if ($cls.ToString() -eq '#32770') {
            $текст = [AccWin]::Title($w)
            $файл = Join-Path $диагностика ("messagebox-" + (Get-Date -Format 'HHmmss') + ".png")
            [AccWin]::Capture($w, $файл) | Out-Null
            Write-Host ("  unexpected message box: '" + $текст + "' -> " + $файл)
            $ок = [AccWin]::FindButton($w, 'ОК')
            if ($ок -ne [IntPtr]::Zero) { [AccWin]::Click($ок); $закрыто++ }
            continue
        }
        $заголовок = [AccWin]::Title($w)
        if ($заголовок -eq 'Подача конвертов') {
            $файл = Join-Path $диагностика ("feed-" + (Get-Date -Format 'HHmmss') + ".png")
            [AccWin]::Capture($w, $файл) | Out-Null
            # «Продолжить» ведёт в окно предпросмотра; физический спул
            # вызывается только после подтверждения предпросмотра, поэтому
            # задание на принтер не уходит.
            Write-Host ("  feed dialog: '" + $заголовок + "' -> Продолжить -> " + $файл)
            $продолжить = [AccWin]::FindButton($w, 'Продолжить')
            if ($продолжить -ne [IntPtr]::Zero) { [AccWin]::Click($продолжить); $закрыто++ }
            continue
        }
        if ($заголовок -eq 'Проверка размера бумаги') {
            $файл = Join-Path $диагностика ("papersize-" + (Get-Date -Format 'HHmmss') + ".png")
            [AccWin]::Capture($w, $файл) | Out-Null
            Write-Host ("  paper-size dialog: '" + $заголовок + "' -> Отмена -> " + $файл)
            $отмена = [AccWin]::FindButton($w, 'Отмена')
            if ($отмена -eq [IntPtr]::Zero) { $отмена = [AccWin]::FindButton($w, 'Выбрать другой принтер') }
            if ($отмена -ne [IntPtr]::Zero) { [AccWin]::Click($отмена); $закрыто++ }
        }
    }
    return $закрыто
}

# Дамп всех видимых окон процесса: класс | заголовок
function Dump-AllWindows([uint32]$procId) {
    foreach ($w in [AccWin]::WindowsOf($procId)) {
        $cls = New-Object System.Text.StringBuilder 256
        [AccWin]::GetClassName($w, $cls, 256) | Out-Null
        Write-Host ("  window: " + $cls.ToString() + " | '" + [AccWin]::Title($w) + "'")
    }
}
