# CursorJump 再レビュー結果

## Finding 1: [P1] 削除モード系の同期イベント発火がまだ残っている

対象:
- `src/CursorJump.App/MouseHookService.cs:255-277`
- `src/CursorJump.App/MouseHookService.cs:402-407`

問題:
削除モード中の以下イベントが、まだ低レベルマウスフックコールバック内から直接発火されている。

- `DeleteAllConfirmRequested`
- `DeleteModeClicked`
- `DeleteModeEscPressed`

また、通常モードの `DisplayDeleteRequested` も直接発火されている。

該当箇所では、イベント発火後に呼び先で以下の重い処理が走る可能性がある。

- 削除モードオーバーレイ生成
- `EnterDeleteMode` / `ExitDeleteMode`
- マーカー再構築
- 座標追加/削除
- `settings.json` 保存

低レベルフックは短時間で戻る必要があるため、このままだと入力遅延や Windows によるフック無効化のリスクが残る。

修正方針:
これらも通常の `SaveRequested` / `NavigateRequested` と同じように `RaiseAsync` へ統一する。

フックコールバック内で同期実行するのは以下だけにする。

- ショートカットのマッチ判定
- swallow フラグ設定
- 必要な座標・イベント情報の確定
- `return (IntPtr)1`

例:
```csharp
SetSwallowUpFlag(pressedButton.Value);
RaiseAsync(DeleteModeClicked, args);
return (IntPtr)1;
```

`EventArgs.Empty` 用のイベントについては、`EventHandler` 向けの `RaiseAsync` overload を追加する。

優先度:
P1

## Finding 2: [P1] キーボード削除モード系も同期発火のまま

対象:
- `src/CursorJump.App/KeyboardHookService.cs:129-154`
- `src/CursorJump.App/KeyboardHookService.cs:189-195`

問題:
`KeyboardHookService` 側でも、削除モード中の以下イベントが直接発火されている。

- `DeleteAllConfirmRequested`
- `DeleteModeClicked`
- `DeleteModeEscPressed`

また、通常モードの `DisplayDeleteRequested` も直接発火されている。

マウス側と同じく、呼び先で削除モード UI や永続化処理が走る可能性があり、低レベルキーボードフック内で重い処理を実行するリスクが残っている。

修正方針:
`MouseHookService` と同じ方針で、削除モード関連イベントも非同期 dispatch する。

`MouseHookEventArgs` を持つイベント:
```csharp
_swallowNextKeyUp.Add(vkCode);
RaiseAsync(DeleteModeClicked, args);
return (IntPtr)1;
```

`EventArgs.Empty` のイベント:
```csharp
_swallowNextKeyUp.Add(vkCode);
RaiseAsync(DeleteModeEscPressed);
return (IntPtr)1;
```

`EventHandler` 用の `RaiseAsync` overload を追加する。

優先度:
P1

## Finding 3: [P2] 保存失敗時の状態不整合はログ追加だけで残っている

対象:
- `src/CursorJump.App/MainWindow.xaml.cs:40-51`

問題:
`SettingsService.Save()` の戻り値を見るようにはなったが、依然として `_settingsService.Current.SavedCoordinatesA/B` を先に直接書き換えてから保存している。

現在の処理:
```csharp
_settingsService.Current.SavedCoordinatesA = _coordinateStore.GetAll().ToList();
if (!_settingsService.Save(_settingsService.Current))
    DebugLog.Write("MainWindow: SavedCoordinatesA persistence failed ...");
```

```csharp
_settingsService.Current.SavedCoordinatesB = _coordinateStoreB.GetAll().ToList();
if (!_settingsService.Save(_settingsService.Current))
    DebugLog.Write("MainWindow: SavedCoordinatesB persistence failed ...");
```

このため、保存失敗時には以下の状態になる。

- `CoordinateStore` は変更済み
- `_settingsService.Current.SavedCoordinatesA/B` も変更済み
- しかし `settings.json` は未更新

ログ追加により失敗は記録されるが、`Current` と永続化ファイルの不整合自体は解消されていない。

修正方針:
保存用コピーを作り、成功時だけ `SettingsService.Current` が差し替わる構造にする。

推奨:
- `AppSettings` のコピー helper を用意する
- 現在設定をコピーする
- コピー側の `SavedCoordinatesA/B` だけ差し替える
- `_settingsService.Save(copy)` を呼ぶ
- `Save()` が成功したときだけ `Current` が更新される

例:
```csharp
var settings = _settingsService.Current.Clone();
settings.SavedCoordinatesA = _coordinateStore.GetAll().ToList();

if (!_settingsService.Save(settings))
{
    DebugLog.Write("MainWindow: SavedCoordinatesA persistence failed. Coordinates may be lost on next app restart.");
}
```

注意:
座標追加/削除はすでに `CoordinateStore` 側で起きた後なので、保存失敗時に UI 上の座標を巻き戻すかどうかは別設計でよい。

最低限、`_settingsService.Current` を保存前に直接 mutate しないこと。

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

## レビュー対象外にした事項

全削除が確認なしで即実行される挙動は、ユーザーの意図した設計として扱う。
そのため、今回の修正対象からは除外する。

---

## 対応記録 (v1.2.1)

Finding 1・2・3 をすべて修正済み。コミット: `fix: フック内同期イベント発火を RaiseAsync に統一・Clone パターンで保存整合性を強化 (v1.2.1)`

### Finding 1 対応 — MouseHookService.cs

`RaiseAsync(EventHandler?)` overload を追加（`DeleteModeEscPressed` など `EventArgs.Empty` 用）:

```csharp
private void RaiseAsync(EventHandler? handler)
{
    if (handler is null) return;
    var dispatcher = Application.Current?.Dispatcher;
    if (dispatcher is null) { handler(this, EventArgs.Empty); return; }
    dispatcher.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
}
```

以下5箇所を `RaiseAsync` に統一:

| 箇所 | イベント |
|---|---|
| `KeyboardHookCallback` (ESC 検知) | `DeleteModeEscPressed` |
| 削除モード優先1 | `DeleteAllConfirmRequested` |
| 削除モード優先2 | `DeleteModeClicked` |
| 削除モード優先3 | `DeleteModeEscPressed` |
| 通常モード DisplayDelete | `DisplayDeleteRequested` |

`SetSwallowUpFlag` / `_swallowNextKeyUp.Add` は `RaiseAsync` の**前**に移動し、
swallow 設定が常に同期で完了することを保証している。

`FireShortcutOnUiThread`（タイマー経由の Chord 発火パス）内の `DisplayDeleteRequested?.Invoke` は
既に `dispatcher.BeginInvoke` の中で実行されているため変更していない。

### Finding 2 対応 — KeyboardHookService.cs

MouseHookService と同じ方針で対応。`RaiseAsync(EventHandler?)` overload を追加し、以下4箇所を統一:

| 箇所 | イベント |
|---|---|
| 削除モード優先1 | `DeleteAllConfirmRequested` |
| 削除モード優先2 | `DeleteModeClicked` |
| 削除モード優先3 | `DeleteModeEscPressed` |
| 通常モード DisplayDelete | `DisplayDeleteRequested` |

### Finding 3 対応 — AppSettings.cs + MainWindow.xaml.cs

`AppSettings.Clone()` を追加:

```csharp
public AppSettings Clone()
{
    var c = (AppSettings)MemberwiseClone();
    c.SavedCoordinatesA = new List<SavedCoordinate>(SavedCoordinatesA);
    c.SavedCoordinatesB = new List<SavedCoordinate>(SavedCoordinatesB);
    return c;
}
```

`MemberwiseClone` でスカラー・参照型フィールドを浅くコピーし、
座標リストのみ新しいインスタンスに複製する。`ActionShortcut` 等の参照型フィールドは
`OnCoordinateStoreAChanged` が差し替えないため浅いコピーで問題なし。

`MainWindow.xaml.cs` の `OnCoordinateStoreAChanged` / `OnCoordinateStoreBChanged` を Clone パターンに変更:

```csharp
// 変更後
private void OnCoordinateStoreAChanged()
{
    var snap = _settingsService.Current.Clone();
    snap.SavedCoordinatesA = _coordinateStore.GetAll().ToList();
    if (!_settingsService.Save(snap))
        DebugLog.Write("...");
}
```

`SettingsService.Save(AppSettings settings)` は成功時のみ内部で `Current = settings` を実行する実装
（`SettingsService.cs:60`）のため、呼び出し側での追加代入は不要。
保存失敗時は `Current` が変更前のスナップショットを保持したままになる。

### 備考

`DisplayDeleteRequested` を非同期化すると、`EnterDeleteMode()` で立てる `_deleteMode` フラグが
非同期で設定されることになる。フックコールバックが `_deleteMode = true` より先に発火する
狭いレースウィンドウが理論上存在するが、実用上は無害と判断して対応した。
（既存の Chord → `BeginInvoke` → `EnterDeleteMode` パスと同じ構造であり、以前から許容されている）
