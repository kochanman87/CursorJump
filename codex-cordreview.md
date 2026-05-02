# CursorJump 再レビュー結果

## Finding 1: [P1] 中ボタン Chord 成立時だけ同期発火が残っている

対象:
- `src/CursorJump.App/MouseHookService.cs:603-605`
- `src/CursorJump.App/MouseHookService.cs:757-770`

問題:
通常クリック系は `RaiseAsync` に寄せられているが、`MiddleLeftChord` / `MiddleRightChord` 成立時だけ別経路になっている。

現在の流れ:
```csharp
FireShortcutOnUiThread(sc, settings, args);
```

`FireShortcutOnUiThread()` は以下のように、Dispatcher 上で呼ばれた場合に即 `fire()` する。

```csharp
if (dispatcher is null || dispatcher.CheckAccess()) fire();
else dispatcher.BeginInvoke(fire);
```

低レベルフックはインストール元スレッドに配送され得るため、WPF Dispatcher スレッド上で `TryHandleMiddleChord()` が動く可能性がある。
その場合、`SaveRequested` / `NavigateRequested` / `DisplayDeleteRequested` などの呼び先で、座標保存、`settings.json` 書き込み、オーバーレイ生成がフック内で同期実行される。

これは、これまで修正してきた「低レベルフック内では重い処理をしない」という方針から漏れている。

修正方針:
フック由来の Chord 成立経路では、常に Dispatcher キューへ投げる。

推奨:
- `FireShortcutOnUiThread()` から `dispatcher.CheckAccess()` 即時実行分岐をなくす
- `dispatcher` がある場合は常に `BeginInvoke`
- もしくは `FireShortcutAsync` のような名前に変えて、必ず非同期 dispatch する helper にする

例:
```csharp
if (dispatcher is null)
{
    fire();
}
else
{
    dispatcher.BeginInvoke(fire);
}
```

注意:
`OnMiddleDeferElapsed()` は ThreadPool タイマーから呼ばれるため現状でも `BeginInvoke` になりやすいが、`TryHandleMiddleChord()` はマウスフックコールバック内から直接呼ばれるため、こちらが問題。

優先度:
P1

## Finding 2: [P1] 削除モードの mousemove がフック内で UI 更新している

対象:
- `src/CursorJump.App/MouseHookService.cs:198-203`
- `src/CursorJump.App/OverlayService.cs:332-358`

問題:
削除モード中の `WM_MOUSEMOVE` で `DeleteModeMoved` を直接発火している。

現在の処理:
```csharp
if (msg == NativeMethods.WM_MOUSEMOVE)
{
    DeleteModeMoved?.Invoke(this, new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y));
    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
}
```

呼び先の `OverlayService.OnDeleteModeMoved()` は以下の WPF UI 更新を行う。

- 前回ハイライトの `StrokeThickness` / `Stroke` 更新
- 近傍マーカー検索
- 新しいハイライトの `StrokeThickness` / `Stroke` 更新
- ヘルプパネルのモニタ追従・再配置

`WM_MOUSEMOVE` は非常に高頻度に発生するため、フック内同期実行のリスクが通常クリックより大きい。
単純に `RaiseAsync(DeleteModeMoved, args)` へ置き換えるだけだと、今度は Dispatcher キューに mousemove が大量に積まれる可能性がある。

修正方針:
mousemove は coalesce / throttle する。

推奨:
- `MouseHookService` 側で最新座標だけ保持する
- Dispatcher への投函が未処理の間は追加投函しない
- Dispatcher 側で最新座標を1回だけ `DeleteModeMoved` として発火する

イメージ:
```csharp
_pendingDeleteMove = new MouseHookEventArgs(x, y);
if (_deleteMoveDispatchQueued) return CallNextHookEx(...);

_deleteMoveDispatchQueued = true;
dispatcher.BeginInvoke(() =>
{
    var latest = _pendingDeleteMove;
    _deleteMoveDispatchQueued = false;
    DeleteModeMoved?.Invoke(this, latest);
});
```

注意:
座標は最新値だけ処理できればハイライト用途として十分。
全 mousemove を忠実に処理する必要はない。

優先度:
P1

## Finding 3: [P2] 削除モード突入時の ESC 用キーボードフック失敗を無視している

対象:
- `src/CursorJump.App/MouseHookService.cs:127-143`

問題:
`EnterDeleteMode()` で ESC 検出用の `WH_KEYBOARD_LL` をインストールしているが、`SetWindowsHookEx` の戻り値が `IntPtr.Zero` でも失敗扱いしていない。

現在の処理:
```csharp
_keyboardHookHandle = NativeMethods.SetWindowsHookEx(
    NativeMethods.WH_KEYBOARD_LL,
    _keyboardHookProc,
    moduleHandle,
    0);
DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
```

`SetWindowsHookEx` が失敗すると、`_deleteMode` は `true` のまま、マーカーオーバーレイも表示されるが、ESC キーで削除モードを抜けられない。
ユーザーは別ショートカットで抜けられる場合もあるが、UI 上 ESC を終了手段として案内しているため、失敗を無視するのは品質上弱い。

修正方針:
`SetWindowsHookEx` 失敗時は明示的に扱う。

候補:
- `Marshal.GetLastWin32Error()` をログに出す
- ESC フックなしでも操作継続するなら、ヘルプ表示で ESC を案内しない
- 失敗時に削除モードへ入らない設計にする
- 少なくとも `_keyboardHookHandle == IntPtr.Zero` を検知し、`DebugLog` に warning と Win32 error を残す

最低限:
```csharp
if (_keyboardHookHandle == IntPtr.Zero)
{
    DebugLog.Write($"KeyboardHook install failed: {Marshal.GetLastWin32Error()}");
}
```

より安全にするなら、`EnterDeleteMode()` の戻り値を `bool` にして、失敗時はオーバーレイ表示側へ伝える。

優先度:
P2

## Finding 4: [P3] キーボード DisplayDelete の KEYUP 消費が削除モード突入で消える可能性がある

対象:
- `src/CursorJump.App/KeyboardHookService.cs:82-87`
- `src/CursorJump.App/KeyboardHookService.cs:189-194`

問題:
通常モードでキーボードの `DisplayDeleteShortcut` が押されると、KEYDOWN 側で `_swallowNextKeyUp.Add(vkCode)` してから `DisplayDeleteRequested` を非同期発火する。

```csharp
_swallowNextKeyUp.Add(vkCode);
RaiseAsync(DisplayDeleteRequested, args);
return (IntPtr)1;
```

その後、`DisplayDeleteRequested` の呼び先で削除モードに入ると、`KeyboardHookService.EnterDeleteMode()` が `_swallowNextKeyUp.Clear()` を実行する。

```csharp
public void EnterDeleteMode()
{
    _deleteMode = true;
    _swallowNextKeyUp.Clear();
}
```

このタイミングが KEYUP より前だと、本来消費すべき `DisplayDeleteShortcut` の KEYUP が消費されず、フォーカス中のアプリへ KEYUP だけ通る可能性がある。

F13-F24 では実害が小さいケースが多いが、低レベルキーボードフックとしては DOWN だけ消費して UP を通す不整合になる。

修正方針:
削除モード突入時に `_swallowNextKeyUp` を無条件 Clear しない。

候補:
- `EnterDeleteMode()` では clear しない
- clear が必要な理由があるなら、少なくとも直前に消費した DisplayDelete の keyup は保持する
- `Suspend()` と `EnterDeleteMode()` で同じ clear 方針にしてよいか再検討する

優先度:
P3

## 確認結果

実行コマンド:
```powershell
dotnet build CursorJump.sln
```

結果:
- ビルド成功
- エラー 0
- 警告 0

## 補足

前回指摘した以下は改善されている。

- マウス削除モードのクリック/キー系イベントは `RaiseAsync` 化済み
- キーボード削除モードのクリック相当イベントは `RaiseAsync` 化済み
- `MainWindow` の座標保存は `AppSettings.Clone()` 経由になり、`SettingsService.Current` の保存前直接 mutate は解消済み

全削除が確認なしで即実行される挙動は、ユーザーの意図した設計として扱う。
そのため、今回の修正対象からは除外する。

---

## 対応記録 (v1.2.2)

Finding 1–4 および自己分析①–④をすべて修正済み。コミット: `fix: Chord同期発火・mousemove throttle・volatile・デッドコード除去 (v1.2.2)`

### Finding 1 対応 — FireShortcutOnUiThread の CheckAccess() 除去

`MouseHookService.FireShortcutOnUiThread()` の `dispatcher.CheckAccess()` による即時実行分岐を除去。

```csharp
// 変更後
if (dispatcher is null) fire();
else dispatcher.BeginInvoke(fire);
```

Chord 発火パス（`TryHandleMiddleChord` → `FireShortcutOnUiThread`）が WPF Dispatcher スレッドから
呼ばれた場合でも、常に `BeginInvoke` でキューに投函される。

### Finding 2 対応 — WM_MOUSEMOVE の throttle 実装

`volatile` フィールド 2 本を追加し、Dispatcher キューに積み過ぎを防ぐ:

```csharp
private volatile MouseHookEventArgs? _pendingDeleteMove;
private volatile bool _deleteMoveDispatchQueued;
```

フックスレッドは最新座標を `_pendingDeleteMove` に書き込み、未処理ディスパッチがなければ
`_deleteMoveDispatchQueued = true` にして `BeginInvoke` する。
UI スレッドはフラグを `false` に戻してから `DeleteModeMoved` を 1 回だけ発火する。

`ExitDeleteMode()` で `_pendingDeleteMove = null; _deleteMoveDispatchQueued = false;` をリセット。

### Finding 3 対応 — EnterDeleteMode キーボードフック失敗ログ

`SetWindowsHookEx` の直後に `IntPtr.Zero` チェックを追加:

```csharp
if (_keyboardHookHandle == IntPtr.Zero)
    DebugLog.Write($"KeyboardHook install failed: Win32Error={Marshal.GetLastWin32Error()}");
```

失敗してもオーバーレイ表示は続行（ESC 以外の終了手段が存在するため）。
失敗時は debug.log に Win32 エラーコードが記録される。

### Finding 4 対応 — KeyboardHookService.EnterDeleteMode の Clear() 除去

`_swallowNextKeyUp.Clear()` を `EnterDeleteMode()` から削除。

理由: `DisplayDeleteRequested` が非同期発火（`RaiseAsync`）になったことで、
KEYDOWN で `_swallowNextKeyUp.Add(vkCode)` してから `EnterDeleteMode()` が呼ばれるまでに
物理 KEYUP が来る可能性がある。`Clear()` があるとその KEYUP を消費できず
アプリへ素通りする。`Suspend()` 側の `Clear()` は設定画面オープン時の意図的なリセットのため変更しない。

### ① volatile 追加

フックスレッド（読み取り）と UI スレッド（書き込み）を跨ぐフィールドに `volatile` を付与:

| ファイル | フィールド |
|---|---|
| `MouseHookService.cs` | `_deleteMode`, `_suspended`, `_pendingDeleteMove`, `_deleteMoveDispatchQueued` |
| `KeyboardHookService.cs` | `_deleteMode`, `_suspended` |
| `SettingsService.cs` | `_current`（自動プロパティを明示バッキングフィールドに変換） |

`SettingsService` は `public AppSettings Current { get; private set; }` を
`private volatile AppSettings _current` + `public AppSettings Current => _current;` に変換。

### ② SettingsChanged 除去

`SettingsService.SettingsChanged` イベントはプロジェクト全体で購読箇所がなかったため削除。
`Save()` 内の `SettingsChanged?.Invoke()` も合わせて除去。

### ③ GetNextInMonitor ダブルルックアップ修正

```csharp
// 変更前
_monitorIndices.TryGetValue(monitorDeviceName, out int lastRawIndex);
if (!_monitorIndices.ContainsKey(monitorDeviceName))
    lastRawIndex = -1;

// 変更後
if (!_monitorIndices.TryGetValue(monitorDeviceName, out int lastRawIndex))
    lastRawIndex = -1;
```

### ④ .tmp ファイルクリーンアップ

`SettingsService.Save()` の `tempPath` 宣言を `try` の外に移動し、`catch` 内で削除を試みる:

```csharp
string tempPath = SettingsPath + ".tmp";
try { ... }
catch (Exception ex)
{
    DebugLog.Write(...);
    try { File.Delete(tempPath); } catch { }
    return false;
}
```

`File.WriteAllText` 成功・`File.Move` 失敗のケースで残留する `.tmp` ファイルを回収する。
