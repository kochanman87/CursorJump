# CursorJump - 開発ガイド

> ユーザー向けの概要・機能一覧・ビルド手順は [README.md](README.md) を参照。

## 技術スタック
- .NET 8.0 (WPF + WinForms)
- C#, WinExe, PerMonitorV2 DPI対応
- 外部NuGetパッケージなし

## プロジェクト構成
```
src/CursorJump.App/
├── App.xaml / App.xaml.cs          # エントリポイント、サービス初期化
├── MainWindow.xaml / .cs           # 不可視ウィンドウ（フック管理）
├── Models/
│   ├── AppSettings.cs              # 設定データモデル（ActionShortcut, TriggerType, ModifierKeyFlags等）
│   └── SavedCoordinate.cs          # 保存座標 record
├── NativeMethods.cs                # Win32 P/Invoke（マウス/キーボードフック、カーソル等）
├── MouseHookService.cs             # WH_MOUSE_LL低レベルフック + 座標表示モード（WH_KEYBOARD_LL併用）
├── KeyboardHookService.cs          # WH_KEYBOARD_LL常時フック（F13-F24キーボードトリガー）
├── CursorService.cs                # カーソル移動（任意座標ジャンプ）
├── CoordinateStore.cs              # 座標リスト管理（Add/GetNext循環/GetNextInMonitor/RemoveAt）
├── OverlayService.cs               # オーバーレイアニメーション（収縮円、軌跡、マーカー）
├── OverlayWindow.xaml / .cs        # 透明オーバーレイウィンドウ基盤
├── DebugLog.cs                     # デバッグログ（%APPDATA%/CursorJump/debug.log）
├── SettingsService.cs              # 設定のJSON読み書き（%APPDATA%/CursorJump/settings.json）
├── SettingsWindow.xaml / .cs       # 設定画面UI
├── TrayIconService.cs              # タスクトレイアイコン管理
├── app.manifest                    # DPI設定、実行レベル
└── CursorJump.App.csproj
```

## アーキテクチャ上の注意点
- **MainWindowは不可視**: Width=0, Height=0, Collapsed。HWNDメッセージ受信専用
- **ShutdownMode=OnExplicitShutdown**: ウィンドウクローズでアプリ終了しない
- **フックのデリゲート**: `_hookProc`/`_keyboardHookProc`をフィールドに保持必須（GC回収防止）
- **UPイベント消費**: DOWNイベント消費時にフラグを立て、対応するUPイベントも消費する（右クリックメニュー抑止）
- **XButtonのUP消費**: `WM_XBUTTONUP`はボタン種別が`mouseData >> 16`で判定が必要（L/R/Mと異なる）
- **座標系**: マウスフック・SetCursorPosは物理ピクセル座標。WPFオーバーレイ描画時はTransformFromDeviceでDIP変換
- **設定ファイル**: `%APPDATA%/CursorJump/settings.json`（System.Text.Json）
- **ログファイル**: `%APPDATA%/CursorJump/debug.log`（起動時モニター情報、フックイベント、DPI変更を記録）
- **MouseButtonType enum**: Left=0, Right=1, Middle=2, XButton1=3, XButton2=4（末尾追加で後方互換性維持）
- **TriggerType [Flags] enum**: `Mouse=1, Keyboard=2`。`EnabledTriggers` に複数フラグをセットすることでOR動作。旧settings.jsonは `EnabledTriggers` 不在 → デフォルト `Mouse` として扱われ後方互換。JSONはJsonStringEnumConverterで文字列保存（例: `"Mouse, Keyboard"`）
- **SavedCoordinate**: `record SavedCoordinate(int X, int Y, string MonitorDeviceName = "")`。保存時に `Screen.FromPoint` でモニタ名を記録。`""` は旧settings.jsonとの後方互換のデフォルト値

### 座標表示/編集モードのアーキテクチャ
- **オーバーレイは表示専用**: clickThrough=true。フォーカス取得しない
- **入力はすべて低レベルフックで処理**: MouseHookServiceの「削除モード」（`EnterDeleteMode`/`ExitDeleteMode`）が、マウスクリック・移動をDeleteModeClicked/DeleteModeMoved イベントとして発火。ESC・右クリックはそれぞれWH_KEYBOARD_LLフック・WH_MOUSE_LLフックで検知し、DeleteModeEscPressedを発火
- **左クリックのハイブリッド動作**: マーカー近く(snapDistance=40px)→削除、それ以外→追加
- **座標比較は物理ピクセル同士**: 保存座標もフック座標も物理ピクセルのため、DPI変換不要で正確

### WPFオーバーレイとフォーカスの既知の制約（重要）
- **WS_EX_LAYERED + WS_EX_TOOLWINDOW のWPFウィンドウは、マウスフックコールバック内から作成するとOSレベルの入力フォーカスを取得できない**。`Activate()`、`SetForegroundWindow`、`AttachThreadInput` いずれも効果なし
- `Ctrl+Alt+Delete→Esc` でデスクトップが再構成されると動作する（=OS側の入力キュー再構築が必要）
- **対策**: ユーザー入力が必要なオーバーレイでは、WPFイベント（MouseDown等）に依存せず、低レベルフック（WH_MOUSE_LL / WH_KEYBOARD_LL）で入力を処理する

### CoverVirtualScreen の座標系
- `SystemParameters.VirtualScreen*` は既にDIP値。`TransformFromDevice` で再変換してはいけない（二重変換バグになる）

## キーボードキートリガー（VIA連携）

VIAキーボードカスタマイズツールのマクロ機能でF13-F24を送信し、CursorJumpのアクションをトリガーできる。マウスボタンとキーボードキーを設定画面で切り替え可能。

### データモデル
```csharp
[Flags]
public enum TriggerType { None = 0, Mouse = 1, Keyboard = 2 }

public sealed class ActionShortcut
{
    public TriggerType EnabledTriggers { get; set; } = TriggerType.Mouse; // OR指定可能
    public ModifierKeyFlags Modifiers { get; set; } = ...;
    public MouseButtonType MouseButton { get; set; } = ...;
    public int VirtualKeyCode { get; set; } = 0;  // VK_F13=0x7C〜VK_F24=0x87
}
```

### サービス構成
- `MouseHookService`（WH_MOUSE_LL）: マウスボタントリガー処理
- `KeyboardHookService`（WH_KEYBOARD_LL）: キーボードトリガー処理（常時インストール）
- 両サービスとも `SaveRequested`/`NavigateRequested`/`NavigateCurrentMonitorRequested`/`DisplayDeleteRequested` を発火
- キーボードトリガー時は `GetCursorPos()` で現在のカーソル座標を取得して `MouseHookEventArgs` に格納
- 削除モード中は `KeyboardHookService` のトリガーを無効化（`EnterDeleteMode`/`ExitDeleteMode`）

### モニタ内ナビゲーション
- `NavigateCurrentMonitorShortcut`（デフォルト `TriggerType.None`＝無効）: ナビゲート時に現在カーソルのモニタ内の座標のみ循環
- `AppSettings.NavigateCurrentMonitorShortcut` を設定画面で有効化してショートカットを割り当てる
- 既存の `NavigateShortcut`（全座標循環）と独立して動作
- `CoordinateStore.GetNextInMonitor(deviceName)`: モニタ別インデックス（`Dictionary<string,int>`）で循環管理。該当モニタ座標が0個の場合 `null` を返す（フォールバックなし）
- `Screen.FromPoint` / `Screen.DeviceName` は物理ピクセル座標ベースで安全に使用可能（DPI変換不要）
