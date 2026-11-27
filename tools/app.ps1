#Requires -Version 7
<#
  常駐中の CursorJump を強制終了せずに終わらせる／起動する（2026-11-27 ユーザー指示「毎回閉じさせず、Claude が判断して閉じて起動・確認して」）。
  CircleSwitch の scripts/app.ps1 と同じ方式。

    stop     CursorJump.App のうち、インストール版か、このリポジトリ配下の exe だけに、Windows のログオフと同じ終了要求を送る
             （全部の窓へ WM_QUERYENDSESSION＋ENDSESSION_CLOSEAPP → WPF の SessionEnding → 普段の終了処理 App.OnExit）
             10 秒で終わらなければ、何もせず失敗を返す（強制終了はしない）
    start    -Target installed（既定）: インストール版 / dev: build_check\app の確認用ビルド / -Exe で直接指定
             Claude アプリ（MSIX）の中から直接起動すると %APPDATA% が専用の写しに振り替えられるため、
             エクスプローラー経由で起動して、本物の設定を読み書きさせる
    status   常駐しているか（パスと起動時刻）を表示する

  単一インスタンス（名前付き Mutex）なので、同時に動くのは 1 つだけ。start の前に stop する。
#>
param(
    [Parameter(Mandatory)][ValidateSet('stop', 'start', 'status')][string]$Task,
    [ValidateSet('installed', 'dev')][string]$Target = 'installed',
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installed = Join-Path $env:LOCALAPPDATA 'CursorJump\current\CursorJump.App.exe'

function Get-Resident {
    @(Get-Process -Name CursorJump.App -ErrorAction SilentlyContinue | Where-Object {
        if (-not $_.Path) { return $false }
        $p = [IO.Path]::GetFullPath($_.Path)
        [string]::Equals($p, $installed, [StringComparison]::OrdinalIgnoreCase) -or
        $p.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    })
}

switch ($Task) {
    'status' {
        $found = Get-Resident
        if ($found.Count -eq 0) { 'not running' }
        else { $found | ForEach-Object { "running pid=$($_.Id) since=$($_.StartTime.ToString('HH:mm:ss')) path=$($_.Path)" } }
    }
    'start' {
        if ((Get-Resident).Count -gt 0) { 'already running（先に stop）'; return }
        if (-not $Exe) { $Exe = if ($Target -eq 'dev') { Join-Path $root 'build_check\app\CursorJump.App.exe' } else { $installed } }
        $Exe = [IO.Path]::GetFullPath($Exe)
        if (-not (Test-Path -LiteralPath $Exe)) { throw "exe がありません: $Exe" }
        # エクスプローラー（Claude アプリの外の既存のシェル）に開かせる
        Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList "`"$Exe`""
        foreach ($i in 1..20) {
            Start-Sleep -Milliseconds 500
            $p = Get-Resident | Where-Object { [string]::Equals([IO.Path]::GetFullPath($_.Path), $Exe, [StringComparison]::OrdinalIgnoreCase) }
            if ($p) { "started pid=$($p[0].Id) path=$Exe"; return }
        }
        throw "10 秒待っても起動を確認できませんでした: $Exe"
    }
    'stop' {
        $found = Get-Resident
        if ($found.Count -eq 0) { 'not running'; return }
        if (-not ('CursorJumpStop.Native' -as [type])) {
            Add-Type -Namespace CursorJumpStop -Name Native -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr hwnd, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr hwnd, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr SendMessageTimeout(System.IntPtr hwnd, uint msg, System.IntPtr wParam, System.IntPtr lParam, uint flags, uint timeout, out System.IntPtr result);
public static System.Collections.Generic.List<System.IntPtr> WindowsOf(uint pid)
{
    var list = new System.Collections.Generic.List<System.IntPtr>();
    EnumWindows((h, _) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); return true; }, System.IntPtr.Zero);
    return list;
}
'@
        }
        $WM_QUERYENDSESSION = 0x11; $WM_ENDSESSION = 0x16; $ENDSESSION_CLOSEAPP = [IntPtr]1; $SMTO_ABORTIFHUNG = 2
        foreach ($p in $found) {
            $path = $p.Path # 終了後は読めなくなるので先に控える
            $windows = [CursorJumpStop.Native]::WindowsOf([uint32]$p.Id)
            foreach ($h in $windows) {
                $r = [IntPtr]::Zero
                [void][CursorJumpStop.Native]::SendMessageTimeout($h, $WM_QUERYENDSESSION, [IntPtr]::Zero, $ENDSESSION_CLOSEAPP, $SMTO_ABORTIFHUNG, 2000, [ref]$r)
                [void][CursorJumpStop.Native]::SendMessageTimeout($h, $WM_ENDSESSION, [IntPtr]1, $ENDSESSION_CLOSEAPP, $SMTO_ABORTIFHUNG, 2000, [ref]$r)
            }
            if ($p.WaitForExit(10000)) { "stopped pid=$($p.Id) (windows=$($windows.Count)) path=$path" }
            else { throw "pid=$($p.Id) が 10 秒で終わりませんでした。強制終了はしていません。トレイのアイコンから終了してください" }
        }
    }
}
