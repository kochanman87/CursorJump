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
│   ├── AppSettings.cs              # 設定データモデル（ActionShortcut, ModifierKeyFlags等）
│   └── SavedCoordinate.cs          # 保存座標 record
├── NativeMethods.cs                # Win32 P/Invoke（マウス/キーボードフック、カーソル等）
├── MouseHookService.cs             # WH_MOUSE_LL低レベルフック + 座標表示モード（WH_KEYBOARD_LL併用）
├── CursorService.cs                # カーソル移動（任意座標ジャンプ）
├── CoordinateStore.cs              # 座標リスト管理（Add/GetNext循環/RemoveAt）
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
- **マウスフックのデリゲート**: `_hookProc`/`_keyboardHookProc`をフィールドに保持必須（GC回収防止）
- **UPイベント消費**: DOWNイベント消費時にフラグを立て、対応するUPイベントも消費する（右クリックメニュー抑止）
- **XButtonのUP消費**: `WM_XBUTTONUP`はボタン種別が`mouseData >> 16`で判定が必要（L/R/Mと異なる）
- **座標系**: マウスフック・SetCursorPosは物理ピクセル座標。WPFオーバーレイ描画時はTransformFromDeviceでDIP変換
- **設定ファイル**: `%APPDATA%/CursorJump/settings.json`（System.Text.Json）
- **ログファイル**: `%APPDATA%/CursorJump/debug.log`（起動時モニター情報、フックイベント、DPI変更を記録）
- **MouseButtonType enum**: Left=0, Right=1, Middle=2, XButton1=3, XButton2=4（末尾追加で後方互換性維持）

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

## 将来の拡張方針（未実装）

### キーボードキートリガー対応（6ボタン目以降・Nape Pro等）
Windows APIのXButtonはXButton1/XButton2のみ。追加ボタンはマウスドライバがキーボードキー（F13-F24等）として送信するため、別のフックが必要。

#### 予定データモデル拡張
```csharp
public enum InputType { Mouse, Keyboard }
public sealed class ActionShortcut
{
    public InputType InputType { get; set; } = InputType.Mouse;  // 追加
    public ModifierKeyFlags Modifiers { get; set; } = ...;
    public MouseButtonType MouseButton { get; set; } = ...;     // InputType==Mouse時
    public int VirtualKeyCode { get; set; } = 0;                // InputType==Keyboard時（追加）
}
```

#### 予定サービス構成
- `MouseHookService`（WH_MOUSE_LL）: 現在のまま維持
- `KeyboardHookService`（WH_KEYBOARD_LL）: 新規作成
- 両サービスとも同じ `SaveRequested`/`NavigateRequested`/`DisplayDeleteRequested` を発火
- `App.xaml.cs` で両サービスのイベントを同じハンドラに接続
