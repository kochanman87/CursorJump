# CursorJump - 開発ガイド

> ユーザー向けの概要・機能一覧・ビルド手順は [README.md](README.md) を参照。

## 技術スタック
- .NET 8.0 (WPF + WinForms)
- C#, WinExe, PerMonitorV2 DPI対応
- 外部NuGetパッケージなし

## 実装時の工数（思考レベル・モデル）選択ガイド

本プロジェクトは小規模 WPF アプリで、変更の大半は「1〜2ファイル・定型的な Win32/WPF パターン」。グローバル基準より軽めに倒してよい。

### 思考レベル（think キーワード）
- **指定なし（デフォルト）で十分**: 定型パターンの追加（Mutex、P/Invoke 1関数追加、設定項目追加、トレイメニュー項目追加、UI 要素追加など）
- **`think` / `think hard` を推奨**: 既存フック処理への条件分岐追加で副作用検討が必要なとき、座標系/DPI に関わる修正
- **`think harder` / `ultrathink` を推奨**: フックアーキテクチャの再設計、WPFオーバーレイとフックの協調に関わる新機能、マルチモニタ×DPI×座標系の新設計、後方互換を伴う設定スキーマ再設計

### モデル
- **Sonnet で十分**: フック処理の分岐追加・設定項目追加・UI 要素追加・定型パターン（Mutex、P/Invoke、JSON プロパティ、トレイ項目）・既存サービスへの機能追加
- **Opus を推奨**: フックアーキテクチャの再設計、低レベルフック × WPF 協調の新機能、複数モニタ/DPI/座標系に関わる新しい仕組みの設計、後方互換を伴う設定スキーマ大改修

## プロジェクト構成
```
src/CursorJump.App/
├── App.xaml / App.xaml.cs          # エントリポイント、テーマ適用、サービス初期化
├── Themes/
│   ├── LightTheme.xaml             # ライトテーマのカラーリソース
│   └── DarkTheme.xaml              # ダークテーマのカラーリソース
├── Styles/
│   └── ModernTheme.xaml            # Fluent風共通スタイル（カード、チップ、トグルスイッチ、ボタン等）
├── ThemeManager.cs                 # Light/Dark テーマの ResourceDictionary 差し替え
├── MainWindow.xaml / .cs           # 不可視ウィンドウ（フック管理）
├── Models/
│   ├── AppSettings.cs              # 設定データモデル（ActionShortcut, TriggerType, ModifierKeyFlags, UiTheme等）
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
- **単一インスタンス制約**: `App.OnStartup` 冒頭で名前付き Mutex (`Local\CursorJump.App.SingleInstance.{GUID}`) による二重起動防止。`Local\` プレフィックスでユーザーセッション単位に限定（RDPマルチセッション可）。2個目起動時は MessageBox 通知後 `Shutdown(0)` で即終了。Mutex は `App` のフィールドとして保持（GC 防止）、`OnExit` で `ReleaseMutex` + `Dispose`。理由: フックの二重発火・settings.jsonの上書き競合・削除モードの不整合を防ぐため
- **フックのデリゲート**: `_hookProc`/`_keyboardHookProc`をフィールドに保持必須（GC回収防止）
- **UPイベント消費**: DOWNイベント消費時にフラグを立て、対応するUPイベントも消費する（右クリックメニュー抑止）
- **XButtonのUP消費**: `WM_XBUTTONUP`はボタン種別が`mouseData >> 16`で判定が必要（L/R/Mと異なる）
- **座標系**: マウスフック・SetCursorPosは物理ピクセル座標。WPFオーバーレイ描画時はTransformFromDeviceでDIP変換
- **設定ファイル**: `%APPDATA%/CursorJump/settings.json`（System.Text.Json）
- **ログファイル**: `%APPDATA%/CursorJump/debug.log`（起動時モニター情報、フックイベント、DPI変更を記録）
- **MouseButtonType enum**: Left=0, Right=1, Middle=2, XButton1=3, XButton2=4, MiddleLeftChord=5, MiddleRightChord=6, MiddleDoubleClick=7, MiddleTripleClick=8（末尾追加で後方互換性維持）。後方4値は「マウスのみで完結するトリガー」用で、修飾キー不要でも割当可
- **中ボタン拡張トリガー（Chord / 多重クリック）**: `MouseHookService` がタイマー遅延で判定する。拡張ボタン（MiddleLeftChord/MiddleRightChord/MiddleDoubleClick/MiddleTripleClick）が**どれか1つでも割り当てられている場合のみ** WM_MBUTTONDOWN を消費して `ChordWindowMs`(200ms) タイマー起動。その間に L/R DOWN が来れば該当 Chord を発火し Middle/L/R の UP を全消費、来なければタイマー満了時にクリック数（`MultiClickWindowMs`=350ms 以内の連続 MDOWN 数）に応じて Triple→Double→Single の順に優先発火。タイマーは ThreadPool スレッドなので `Application.Current.Dispatcher.BeginInvoke` で UI スレッドに復帰してから WPF 側のイベントハンドラを呼ぶ。拡張ボタン未割当時は従来通りの Middle 単押しパスが走り遅延なし。削除モード中は拡張判定に入らず、従来の単押し優先（DisplayDelete=全削除）が動作する
- **Chord 判定で `GetAsyncKeyState(VK_MBUTTON)` は使用禁止**: フックで WM_MBUTTONDOWN を消費（`return (IntPtr)1`）すると OS の非同期キー状態に反映されず、直後の L/R DOWN 時に `GetAsyncKeyState(VK_MBUTTON)` が 0 を返してしまい Chord が発火しない。中ボタン押下状態の判定は `_middleChordHeld` フラグのみで行う（MDOWN 遅延時に true、MUP / Chord 発火 / タイマー満了でクリア）
- **TriggerType [Flags] enum**: `Mouse=1, Keyboard=2`。`EnabledTriggers` に複数フラグをセットすることでOR動作。旧settings.jsonは `EnabledTriggers` 不在 → デフォルト `Mouse` として扱われ後方互換。JSONはJsonStringEnumConverterで文字列保存（例: `"Mouse, Keyboard"`）
- **SavedCoordinate**: `record SavedCoordinate(int X, int Y, string MonitorDeviceName = "")`。保存時に `Screen.FromPoint` でモニタ名を記録。`""` は旧settings.jsonとの後方互換のデフォルト値

### テーマシステム（Light/Dark）
- **App.xaml の MergedDictionaries 先頭（index 0）がテーマ辞書**。`ThemeManager.Apply(UiTheme)` が `Themes/LightTheme.xaml` / `Themes/DarkTheme.xaml` を差し替える
- **全UI要素は `DynamicResource` でテーマリソースを参照**（StaticResource だと実行時切替が効かない）。カラーキー例: `BgPrimaryBrush`, `BgCardBrush`, `TextPrimaryBrush`, `AccentBrush`, `BorderSubtleBrush`
- **共通スタイルは `Styles/ModernTheme.xaml`**: `CardBorder`, `ToggleSwitchStyle`, `ModifierChipStyle`, `PillTabStyle`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `ColorSwatchStyle`, `FluentIcon` 等
- **アイコンは Segoe Fluent Icons フォント**（Win11標準）+ MDL2 Assetsをフォールバック
- **SettingsWindow のタブ切替**: `RadioButton` + `GroupName` で排他制御。`Checked` イベントで `ScrollViewer` の `Visibility` を切替
- **起動時テーマ適用**: `App.OnStartup` → `SettingsService.Load()` → `ThemeManager.Apply(Current.UiTheme)` の順
- **キャンセル時のテーマロールバック**: SettingsWindow で Dark に切替 → キャンセルすると元のテーマに戻すため、`OnCancelClick` で `ThemeManager.Apply(_settingsService.Current.UiTheme)` を呼ぶ

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

## 既知の問題（未対応）

- **MiddleRightChord（ホイール+右クリック）発火時にコンテキストメニューが表示される**: Chord 成立時に `_swallowNextRightUp = true` を立てて RUP を消費しているが、実際にはメニューが出てしまう。WM_RBUTTONDOWN 自体の消費タイミングや、Chord 判定経路と通常の RDOWN 消費経路の関係を再確認する必要がある。別セッションで調査・修正予定
