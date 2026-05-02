# CursorJump 再レビュー結果

## Finding 1: [P2] GetLastWin32Error をログ出力後に読んでいる

対象:
- `src/CursorJump.App/MouseHookService.cs:142-149`

問題:
`SetWindowsHookEx` 失敗時のログは追加されているが、直前に `DebugLog.Write` を呼んでから `Marshal.GetLastWin32Error()` を読んでいる。

現在の処理:
```csharp
_keyboardHookHandle = NativeMethods.SetWindowsHookEx(
    NativeMethods.WH_KEYBOARD_LL,
    _keyboardHookProc,
    moduleHandle,
    0);
DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
if (_keyboardHookHandle == IntPtr.Zero)
    DebugLog.Write($"KeyboardHook install failed: Win32Error={Marshal.GetLastWin32Error()}");
```

`DebugLog.Write` は内部で `File.AppendAllText` を実行するため、そこで発生する Win32 呼び出しにより last-error が上書きされる可能性がある。

その場合、`SetWindowsHookEx` の失敗原因とは違うエラーコードが debug.log に残る。

修正方針:
`SetWindowsHookEx` の直後に `Marshal.GetLastWin32Error()` を保存する。

例:
```csharp
_keyboardHookHandle = NativeMethods.SetWindowsHookEx(
    NativeMethods.WH_KEYBOARD_LL,
    _keyboardHookProc,
    moduleHandle,
    0);
int hookError = _keyboardHookHandle == IntPtr.Zero
    ? Marshal.GetLastWin32Error()
    : 0;

DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
if (_keyboardHookHandle == IntPtr.Zero)
    DebugLog.Write($"KeyboardHook install failed: Win32Error={hookError}");
```

優先度:
P2

## Finding 2: [P2] EnterDeleteMode が多重呼び出し時にキーボードフックを上書きする

対象:
- `src/CursorJump.App/MouseHookService.cs:131-149`

問題:
`EnterDeleteMode()` は既存の `_keyboardHookHandle` を確認せずに毎回 `SetWindowsHookEx` し、戻り値でフィールドを上書きしている。

`DisplayDeleteRequested` が非同期 dispatch になったため、短時間に複数回キューへ積まれると `EnterDeleteMode()` が再入する可能性がある。

その場合:
1. 1回目の `EnterDeleteMode()` がキーボードフック A をインストール
2. 2回目の `EnterDeleteMode()` がキーボードフック B をインストール
3. `_keyboardHookHandle` が B で上書きされる
4. A のハンドルを失い、`ExitDeleteMode()` で unhook できない

結果として、古い ESC 用キーボードフックが残留する可能性がある。

修正方針:
`EnterDeleteMode()` に再入ガードを入れる。

候補:
```csharp
if (_keyboardHookHandle != IntPtr.Zero)
{
    DebugLog.Write("KeyboardHook already installed; skipping reinstall");
    return;
}
```

または、`_deleteMode` が既に `true` の場合はモード突入処理全体をスキップする。

例:
```csharp
if (_deleteMode)
{
    DebugLog.Write("MouseHookService: EnterDeleteMode() ignored because already in delete mode");
    return;
}

_deleteMode = true;
```

ただし、`_deleteMode == true` でも `_keyboardHookHandle == IntPtr.Zero` の失敗状態をどう扱うかは設計すること。

最低限:
- 既存の `_keyboardHookHandle` を上書きしない
- 既存フックがあるなら再インストールしない
- 失敗時の Win32 error は Finding 1 の通り、API 直後に保存する

優先度:
P2

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

前回指摘した以下は対応済み。

- Chord 経路の同期発火: `CheckAccess()` 分岐が消えており対応済み
- 削除モード mousemove: Dispatcher coalesce が入り、フック内 UI 更新は解消
- ESC 用キーボードフック失敗ログ: 対応済み。ただし Finding 1 の通り last-error 取得順に問題あり
- `KeyboardHookService.EnterDeleteMode()` の `_swallowNextKeyUp.Clear()` 除去: 対応済み
- `SettingsChanged` 削除、`.tmp` cleanup、`GetNextInMonitor` 改善: 大きな問題は見当たらない

全削除が確認なしで即実行される挙動は、ユーザーの意図した設計として扱う。
そのため、今回の修正対象からは除外する。

---

## 対応記録 (v1.2.2 patch)

Finding 1・2 をともに `MouseHookService.EnterDeleteMode()` に適用済み。バージョン変更なし（前コミットの PATCH 内修正として扱う）。

### Finding 1 対応 — GetLastWin32Error を API 直後に保存

`SetWindowsHookEx` の戻り値を確認した直後（`DebugLog.Write` より前）にエラーコードを変数に退避する。

```csharp
int hookError = _keyboardHookHandle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
if (_keyboardHookHandle == IntPtr.Zero)
    DebugLog.Write($"KeyboardHook install failed: Win32Error={hookError}");
```

`DebugLog.Write` 内部の `File.AppendAllText` が last-error を上書きする前に保存することで、正確なエラーコードが記録される。

### Finding 2 対応 — EnterDeleteMode の再入ガード

`_deleteMode` が既に `true` の場合は早期リターンする。

```csharp
if (_deleteMode)
{
    DebugLog.Write("MouseHookService: EnterDeleteMode() ignored (already in delete mode)");
    return;
}
```

`DisplayDeleteRequested` が非同期 `BeginInvoke` 経由で複数回 Dispatcher キューに積まれても、
2回目以降は `_keyboardHookHandle` を上書きせずにスキップされる。
`_deleteMode == false` の場合のみ `SetWindowsHookEx` が実行されるため、フックの多重インストールを防ぐ。
