# CursorJump - 開発ガイド

> ユーザー向けの概要・機能一覧・ビルド手順は [README.md](README.md) を参照。

## 技術スタック
- .NET 8.0 (WPF + WinForms)
- C#, WinExe, PerMonitorV2 DPI対応
- 外部NuGetパッケージは [Velopack](https://github.com/velopack/velopack)（自動更新）のみ

## バージョニング
- バージョンは `src/CursorJump.App/CursorJump.App.csproj` の `<Version>` で `MAJOR.MINOR.PATCH`（例: `1.1.0`）形式で管理する
- **機能追加・バグ修正・品質改善などコード変更を行ったときは、コミット前に必ず `<Version>` を更新する**（更新漏れが頻発しているため明示）
  - 後方互換を壊す変更 → MAJOR を上げる
  - 新機能追加 → MINOR を上げる、PATCH を 0 にリセット
  - バグ修正・内部品質改善・リファクタリング → PATCH を上げる
- 設定画面の「情報」タブは `Assembly.GetExecutingAssembly().GetName().Version` で参照しているため、`<Version>` 更新だけで自動的に反映される

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
├── Localization/
│   ├── StringsJa.xaml              # 日本語 UI 文字列（ResourceDictionary）
│   └── StringsEn.xaml              # 英語 UI 文字列（同キー）
├── LocalizationManager.cs          # 言語の ResourceDictionary 差し替え + Loc.Get ヘルパー
├── MainWindow.xaml / .cs           # 不可視ウィンドウ（フック管理）
├── Models/
│   ├── AppSettings.cs              # 設定データモデル（ActionShortcut, TriggerType, ModifierKeyFlags, UiTheme, UiLanguage等）
│   └── SavedCoordinate.cs          # 保存座標 record
├── NativeMethods.cs                # Win32 P/Invoke（マウス/キーボードフック、カーソル等）
├── MouseHookService.cs             # WH_MOUSE_LL低レベルフック + 座標表示モード（WH_KEYBOARD_LL併用）
├── KeyboardHookService.cs          # WH_KEYBOARD_LL常時フック（F13-F24キーボードトリガー）
├── CursorService.cs                # カーソル移動（任意座標ジャンプ）
├── CoordinateStore.cs              # 座標リスト管理（Add/GetNext循環/GetNextInMonitor/RemoveAt/Load/Changed）
├── OverlayService.cs               # オーバーレイアニメーション（収縮円、軌跡、マーカー）
├── OverlayWindow.xaml / .cs        # 透明オーバーレイウィンドウ基盤
├── DebugLog.cs                     # デバッグログ（%APPDATA%/CursorJump/debug.log）
├── SettingsService.cs              # 設定のJSON読み書き（%APPDATA%/CursorJump/settings.json）
├── SettingsWindow.xaml / .cs       # 設定画面UI（設定 / 使い方 / 情報 タブ）
├── UpdateService.cs                # Velopack による GitHub Releases 自動更新
├── UpdateDialog.xaml / .cs         # 新版検出時の確認ダイアログ
├── TrayIconService.cs              # タスクトレイアイコン管理（更新確認メニュー含む）
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
- **設定ファイル**: `%APPDATA%/CursorJump/settings.json`（System.Text.Json）。`AppSettings.SavedCoordinatesA` / `SavedCoordinatesB` に Set A/B の座標が永続化される。`CoordinateStore.Changed` イベントで `MainWindow` が `_settingsService.Save()` を呼び戻し書きする
- **第2座標セット（Set B）**: Win+Shift+左/右 デフォルトの**完全独立**な座標リスト（旧デフォルト Win+Alt は Xbox Game Bar プレフィックスと干渉するため Win+Shift に変更）。`CoordinateStore` を Set A/B で2インスタンス保持し、`SaveShortcutB`/`NavigateShortcutB` でトリガー。`MouseHookService`/`KeyboardHookService` はそれぞれ `SaveRequestedB`/`NavigateRequestedB` イベントを発火。**保存・移動のみ独立**で、削除モード/モニタ内ナビは Set A のみ。削除モードでは `OverlayService.ShowCoordinateMarkers` が `(CoordinateStore, Color)` のリストを受け取り、Set A=`MarkerColor`、Set B=`MarkerColorB` で**両方同時表示・両方削除可能**（デフォルトは同色だがユーザーが個別変更可）。空きエリアクリックでの追加は先頭ストア（Set A）に入る
- **`AreModifiersHeld` は完全一致判定**: 設定された修飾キーが押されており、かつ設定にない修飾キーが**押されていないこと**を要求する（v1.2.3 修正。以前は superset 判定で Ctrl+Win 設定時に Ctrl+Win+Alt でも発火していた）。Alt 判定は Win+Alt 時に OS が VK_LMENU/VK_RMENU の async 状態をクリアするため、汎用 VK_MENU もフォールバックに含める。`[Flags]` enum の `HasFlag` 片側分岐でなく `isDown != required.HasFlag(X)` の対称判定で実装する
- **軌跡エフェクト詳細設定**: `TrailThickness`(1–20dp) / `TrailDurationMs`(100–3000ms) / `TrailOpacity`(0.1–1.0) を設定画面のスライダーで調整可能。`OverlayService.ShowTrail` は単一 `Line` ではなく **12 セグメントに分割した `Line` 群** を描画し、`DoubleAnimation.BeginTime` を移動元側ほど 0、移動先側ほど `duration*0.5` までずらすことで**遠端（移動元）から段階的にフェードアウト**する演出を実現する。各セグメントの実フェード時間は `duration*0.5`、合計再生時間は `duration` に揃う
- **ログファイル**: `%APPDATA%/CursorJump/debug.log`（起動時モニター情報、フックイベント、DPI変更を記録）
- **MouseButtonType enum**: Left=0, Right=1, Middle=2, XButton1=3, XButton2=4, MiddleLeftChord=5, MiddleRightChord=6, MiddleDoubleClick=7, MiddleTripleClick=8（末尾追加で後方互換性維持）。後方4値は「マウスのみで完結するトリガー」用で、修飾キー不要でも割当可
- **中ボタン拡張トリガー（Chord / 多重クリック）**: `MouseHookService` がタイマー遅延で判定する。拡張ボタン（MiddleLeftChord/MiddleRightChord/MiddleDoubleClick/MiddleTripleClick）が**どれか1つでも割り当てられている場合のみ** WM_MBUTTONDOWN を消費して `ChordWindowMs`(200ms) タイマー起動。その間に L/R DOWN が来れば該当 Chord を発火し Middle/L/R の UP を全消費、来なければタイマー満了時にクリック数（`MultiClickWindowMs`=350ms 以内の連続 MDOWN 数）に応じて Triple→Double→Single の順に優先発火。タイマーは ThreadPool スレッドなので `Application.Current.Dispatcher.BeginInvoke` で UI スレッドに復帰してから WPF 側のイベントハンドラを呼ぶ。拡張ボタン未割当時は従来通りの Middle 単押しパスが走り遅延なし。削除モード中は拡張判定に入らず、従来の単押し優先（DisplayDelete=全削除）が動作する
- **Chord 成立時に `_middleChordHeld` はクリアしない**: Chord 発火後もホイールが物理的に押下中の間は判定を継続する必要があるため（ホイール押下のまま L/R を連打したときに2回目以降の Chord を発火させる）、`_middleChordHeld` のクリアは MUP 到達時のみ行う。Chord 発火時はタイマー dispose と `_middleClickCount=0` のみでよい
- **拡張トリガー割当時の中ボタン単押しフォールバック**: MDOWN を一律消費する都合で、Chord/多重クリックいずれにも該当しない単押しは Chrome のタブクローズ等アプリの通常動作を壊してしまう。対策としてタイマー満了時 `count==1` かつ単押しショートカット未マッチ、かつ中ボタンが既に離されている場合に限り `SendInput` で `MOUSEEVENTF_MIDDLEDOWN+MIDDLEUP` を合成再送する。合成入力は `MSLLHOOKSTRUCT.flags & LLMHF_INJECTED` が立つので `HookCallback` 冒頭で素通しさせ、再遅延・無限ループを防ぐ。押下継続中（autoscroll 等）は合成しない（長押し用途と区別不能なため静かに飲み込む従来動作）
- **Chord 判定で `GetAsyncKeyState(VK_MBUTTON)` は使用禁止**: フックで WM_MBUTTONDOWN を消費（`return (IntPtr)1`）すると OS の非同期キー状態に反映されず、直後の L/R DOWN 時に `GetAsyncKeyState(VK_MBUTTON)` が 0 を返してしまい Chord が発火しない。中ボタン押下状態の判定は `_middleChordHeld` フラグのみで行う（MDOWN 遅延時に true、MUP 到達時のみクリア）
- **ホイール長押し → L/R クリックで Chord 発火**: `ChordWindowMs` 満了時に中ボタンがまだ押下中（`_middleChordHeld == true`）であれば、`_middleClickCount` のみクリアして `_middleChordHeld` は true のまま維持する（`OnMiddleDeferElapsed` の冒頭分岐）。これにより押下時間に制限されず、ホイールを押したままの L/R DOWN を `TryHandleMiddleChord` が捕捉する。中ボタンが既に離されている場合のみ従来通り Triple/Double/Single 判定＋合成再送を実行する
- **`EnterDeleteMode`/`ExitDeleteMode` は swallow フラグをクリアしない**: `_swallowNextLeftUp`/`_swallowNextRightUp` は対応する UP 到達時に自然消費される。削除モード遷移時にクリアすると、Chord 成立 → `BeginInvoke` → `EnterDeleteMode` → 物理 RUP という非同期経路で直前に立てたフラグが潰され、コンテキストメニューが出る不具合を招く
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

### ローカライズシステム（日本語/English）
- **App.xaml の `MergedDictionaries[1]` が言語辞書**（index 0 はテーマ、index 2 は ModernTheme.xaml）。`LocalizationManager.Apply(UiLanguage)` が `Localization/StringsJa.xaml` / `Localization/StringsEn.xaml` を差し替える。テーマシステムと同じ構造で、`DynamicResource` 参照により全 UI が即時再評価される
- **全 UI 文字列は `{DynamicResource Str.Xxx}` で参照**。XAML に直書きしてはいけない（言語切替に追従できなくなる）
- **C# からの取得は `Loc.Get("Str.Xxx")`**: 内部で `Application.Current.TryFindResource` を使用。UI スレッド外からの呼出は `Dispatcher.Invoke` で UI スレッドに復帰してから取得する。キーが見つからない場合はキー文字列自体を返す（デバッグ用）
- **キー命名規約**: 階層を `.` 区切り、先頭は常に `Str.`。例: `Str.Settings.Card.Save`、`Str.Button.Left`、`Str.MessageBox.MouseHookFailedFormat`、`Str.Overlay.DeleteMode.Title`
- **`UiLanguage` enum**: `Auto / Japanese / English`。`Auto` は `LocalizationManager.Resolve()` が `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"` で日本語、それ以外で英語にフォールバック解決する。設定画面のラジオは「日本語 / English」の2択のみ表示し、`Auto` は明示的にラジオを選ばない場合（=既存設定の維持）にのみ使う
- **言語切替時の即時反映**: `LocalizationManager.LanguageChanged` イベントが発火される。`TrayIconService` がこれを購読し WinForms の `ToolStripMenuItem.Text` を再設定（DynamicResource が効かないため）。`SettingsWindow` も同イベントで `Title` を再生成し、ボタン名 ComboBox を強制リフレッシュする
- **ボタン名 ComboBox は `ButtonOption` クラス経由**: `ToString()` が現在言語の `Loc.Get(ResourceKey)` を返す wrapper。enum 値 (`MouseButtonType`) と表示文字列を分離し、保存・復元は enum で行う。言語切替時は `ItemsSource` を一度 null にしてから再代入することで ComboBox の表示を強制更新する
- **MessageBox 文言は `string.Format(Loc.Get("Str.Xxx.Format"), arg)` のパターン**: プレースホルダ `{0}` は例外メッセージ等の挿入用（例: `Str.MessageBox.MouseHookFailedFormat`）
- **削除モードヘルプの compact ボタン名**: `ShortcutFormatter.FormatForDeleteMode` は通常名 (`左クリック` / `Left click`) を使うが、Chord/多重クリック由来の物理ボタン名は別キー (`Str.Button.Compact.MiddleLeftChord` 等) で短縮表示する
- **キャンセル時の言語ロールバック**: `OnCancelClick` で `LocalizationManager.Apply(_settingsService.Current.UiLanguage)` を呼ぶ（テーマと同じ）
- **起動時の適用順**: `App.OnStartup` → `SettingsService.Load()` → `ThemeManager.Apply(...)` → `LocalizationManager.Apply(...)` → `MainWindow` 生成
- **キーボードトリガーの修飾キー表記**: `KeyboardHookService` は `VirtualKeyCode` 単独一致で発火するため、UI 上で「Ctrl+F15」と表示されていても実挙動は F15 単独。これは仕様（VIA マクロで修飾キー同時押下を保証できないため）

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
- **キーボードトリガーの修飾キーは表示上のみで実挙動では無視する（仕様）**: `ActionShortcut.Modifiers` は UI 構造上マウス/キーボードで共通だが、`KeyboardHookService.IsKeyboardShortcutMatch` は `VirtualKeyCode` 一致のみで判定する。VIA マクロで F13-F24 を送るユースケースでは修飾キーの同時押下保証が難しいため、意図的に VK 単独一致としている。UI 上で `Ctrl+Win+F15` と表示されていても実際は `F15` 単独で発火する。これはバグではない

### 削除モード中のマウスマッチング
- `MouseHookService.IsShortcutMatchForDeleteMode` は、ショートカットが拡張ボタンに割り当てられている場合でも、削除モード中は物理ボタン単押しにマップして判定する（`MiddleLeftChord`/`MiddleDoubleClick`→Left、`MiddleRightChord`/`MiddleTripleClick`→Right）。理由: 削除モード中はオーバーレイ表示中の素早い操作が求められるため、Chord や多重クリックの待機時間を挟まずに即応したい。DoubleClick/TripleClick を Left/Right に分けることで、両方を同時に割り当てても競合しない。通常モード中の拡張トリガー挙動はそのまま維持される
- 削除モードヘルプ表示は `ShortcutFormatter.FormatForDeleteMode()` を使用する。修飾キーなし・拡張ボタンは上記の物理ボタン名（「左クリック」「右クリック」）に変換して表示することで、実際の操作と一致させる

### モニタ内ナビゲーション
- `NavigateCurrentMonitorShortcut`（デフォルト `TriggerType.None`＝無効）: ナビゲート時に現在カーソルのモニタ内の座標のみ循環
- `AppSettings.NavigateCurrentMonitorShortcut` を設定画面で有効化してショートカットを割り当てる
- 既存の `NavigateShortcut`（全座標循環）と独立して動作
- `CoordinateStore.GetNextInMonitor(deviceName)`: モニタ別インデックス（`Dictionary<string,int>`）で循環管理。該当モニタ座標が0個の場合 `null` を返す（フォールバックなし）
- `Screen.FromPoint` / `Screen.DeviceName` は物理ピクセル座標ベースで安全に使用可能（DPI変換不要）

## ライセンスシステム（Free / Pro 版）

v1.4.0 で Pro/Free 版を導入。BOOTH 配布のライセンスキーを設定画面で適用すると Pro 化される。

### Free 版の制限
- 座標保存は **3 個まで**（`LicenseService.FreeMaxCoordinates`）。Set A のみが対象。上限到達時の追加要求は静かに無視（DebugLog のみ）。削除モードで 1 個削除すれば再保存可能
- **Set B（第2座標セット）は機能無効**: `MainWindow.OnSaveRequestedB` / `OnNavigateRequestedB` で `IsPro == false` なら早期 return。設定画面で Set B のショートカットは編集可能だが、トリガーは反応しない（PRO バッジ + 案内テキストで明示）
- 設定画面の Set B セクションに **`PRO` バッジ** と「Pro 版でのみ動作します」ロック通知が表示される（Pro 化で消える）

### キー設計（秀丸方式 + ハッシュ埋め込み）
- **キー文字列**: 全購入者共通の固定値。**平文値はこのリポジトリには絶対に書かない**（BOOTH 配信 .txt と手元の `.secrets/key.txt`（gitignore 済み）のみに保管）
- **ソース埋め込み**: SHA256 ハッシュのみ（`LicenseService.cs` の `ProKeyHash` 定数）。OSS リポでも安全
  - 検証は `SHA256(UTF8(input.Trim())) → 小文字hex` を `ProKeyHash` と比較
- **平文キーをコメント・ドキュメント・README・CLAUDE.md・テストデータ等のコミット対象ファイルに書くことは禁止**。コードコメントやドキュメンテーションも `git grep` で発見されるため「ハッシュ化したから安全」は成立しない
- **pre-commit hook で機械的に防御**: `.git/hooks/pre-commit` で平文キー文字列を grep し、検出時にコミット拒否
- **逆コンパイル耐性は限定的**（dnSpy で検証メソッドを `return true;` に書き換えれば突破可能）。これは受容する。難読化は将来の選択肢
- **失効・再発行はしない**。リーク発覚時は次のメジャーバージョンで `ProKeyHash` を変更し、BOOTH のメッセージ機能で正規購入者に新キーを一斉通知する運用を想定

### コンポーネント
- **`LicenseService`** (`src/CursorJump.App/LicenseService.cs`): `IsPro`（bool）/ `Status`（NotEntered/Valid/Invalid）/ `Apply(string key)` / `Refresh()` / `Clear()` / `StatusChanged` イベント。`Apply` で Valid なら settings.json に `LicenseKey` を保存。Invalid/NotEntered の場合は保存しない（誤入力で既存ライセンスを潰さないため）
- **`UpgradeDialog`** (`src/CursorJump.App/UpgradeDialog.xaml`): Pro 案内モーダル。`UpgradeReason.SetB` / `UpgradeReason.SaveLimit` で本文を切り替え。`OpenLicenseTabRequested` プロパティで呼出側がライセンスタブを開く判断材料を返す
- **`SettingsWindow`**: 「ライセンス」タブ追加。ステータステキスト・キー入力・適用ボタン・BOOTH 購入リンクを配置。`UpdateLicenseUI()` で Pro/Free 状態を反映（PRO バッジの表示制御も含む）
- **`AppSettings.LicenseKey`**: 入力されたキー文字列を永続化。`SettingsWindow.OnSaveClick` では `_settingsService.Current.LicenseKey` を維持して上書きしない（`LicenseService.Apply` が排他的に書き込む）
- **`TrayIconService`**: コンテキストメニュー先頭に「CursorJump — Free 版 / Pro 版」のラベル（`ToolStripLabel` / Enabled=false）を表示。`LicenseService.StatusChanged` を購読して即時反映

### 設計ポリシー
- **Free 上限到達時の UX**: トースト・モーダルは出さず、保存もエフェクトも実行せず DebugLog のみ。理由は「作業中断を避ける」「設定画面の Free 表記とトレイのラベルで気付いてもらう」設計
- **Set B 編集は技術的に許可**: PRO バッジ + ロック通知の静的表示で意図を伝え、リアルタイム変更傍受モーダルは実装しない（複数コントロールにまたがる傍受は複雑化の割に得るものが薄い）
- **ライセンス検証の呼出**: 起動時に `LicenseService` コンストラクタ内で `Refresh()`、`Apply` 時に再評価。`StatusChanged` で `TrayIconService` のラベルが更新される

## 自動更新（Velopack + GitHub Releases）

GitHub Releases を配布チャネルとし、起動時に新版を検出してユーザー確認後にダウンロード→再起動する仕組み。実装は `UpdateService` + `UpdateDialog` + `Velopack` ライブラリ。

### コンポーネント
- **`UpdateService`** (`src/CursorJump.App/UpdateService.cs`): `Velopack.UpdateManager` を `GithubSource("https://github.com/kochanman87/CursorJump")` で初期化。`CheckForUpdatesAsync()` は新版があれば `UpdateInfo`、無ければ null。例外（オフライン・GitHub障害・未インストール=`IsInstalled==false`）は内部で握りつぶして null を返し `DebugLog` に記録する（呼出側で起動を妨げない設計）。チェック完了時に `AppSettings.LastUpdateCheckUtc` を ISO 8601 で更新する
- **`UpdateDialog`** (`src/CursorJump.App/UpdateDialog.xaml`): 現在版/新版/リリースノート（Markdown生表示）+ 「今すぐ更新」「後で」「このバージョンをスキップ」の3ボタン。「スキップ」は `AppSettings.SkippedVersion` に対象バージョンを書き込み、起動時通知を抑制する
- **起動時チェック** (`App.xaml.cs OnStartup`): `_settingsService.Current.AutoUpdateEnabled` が true のとき `Task.Run` で fire-and-forget チェック。新版があり、かつ `SkippedVersion` と一致しなければ Dispatcher 経由で `UpdateDialog.ShowDialog()`
- **手動チェック**: 設定画面「情報」タブの「今すぐ更新を確認」ボタン、およびトレイメニューの「更新を確認」項目から呼び出せる

### Velopack 統合の要点
- **`VelopackApp.Build().Run()` を `OnStartup` の最先頭で呼ぶ**: Velopack はインストーラ・アップデータが本体 exe を `--veloapp-install` 等の引数で起動して短命に終了させるサブプロセスを使う。Run() がこの引数を検出するとフックを実行して即 `Environment.Exit` する。**Mutex 取得より前**に置く必要がある（インストーラ呼び出しは独自プロセスで Mutex 競合させたくない）
- **開発実行（`dotnet run` / `bin\Debug` 直起動）では `UpdateManager.IsInstalled == false`**: この状態で `CheckForUpdatesAsync` を呼ぶと例外。`UpdateService` 側で `IsInstalled` チェックを行い、インストール経由でない場合は静かに null を返す
- **設定の永続化**: `LastUpdateCheckUtc` / `SkippedVersion` は `UpdateService` が直接 `_settingsService.Save(Clone())` で書き込む。`SettingsWindow.OnSaveClick` でも `Current` 側の値を `AppSettings` に維持コピーすることで、設定画面保存と並行した書き込み競合を避ける（`AutoUpdateEnabled` のみ UI から反映）

### リリース手順（手動）
1. `<Version>` を更新してコミット（CLAUDE.md の バージョニング 規約に従う）
2. `docs/release-notes/v<VERSION>.md` を作成（バージョン別。`vpk pack --releaseNotes` が 1 ファイル全体を nuspec の `<releaseNotes>` に埋め込むため、バージョンごとに分けるのが必須）
3. `dotnet tool install -g vpk`（初回のみ）
4. `dotnet publish src/CursorJump.App/CursorJump.App.csproj -c Release -r win-x64 --self-contained -o publish/`
5. `vpk pack --packId CursorJump --packVersion <VERSION> --packDir publish/ --mainExe CursorJump.App.exe --releaseNotes docs/release-notes/v<VERSION>.md`
6. 生成された `Releases/Setup.exe` と `*-full.nupkg` と `RELEASES` を GitHub Releases にアップロード（タグは `v<VERSION>` 形式）
7. GitHub Release Body にも同 Markdown を貼る（人間が Releases ページで読む用。Velopack のダイアログ表示には影響しない＝nuspec の埋め込み値が使われる）

> **重要**: 手順 5 で `--releaseNotes` を省略すると `<releaseNotes>` が空のまま埋め込まれ、アプリ内アップデートダイアログのリリースノート欄が `-`（空）表示になる。GitHub Release Body をいくら書いても直らない（Velopack は Body を参照しない）。v1.4.2 でこの事故が発生したため v1.4.3 で恒久対策。

### 設計上の注意
- **コード署名なし → SmartScreen 警告**: 初回ダウンロード時に Microsoft Defender SmartScreen が「不明な発行元」警告を出す。回避にはコード署名証明書（年額数万円〜）が必要。導入は別タスク
- **AutoUpdate デフォルトは ON**: 旧 `settings.json` には `AutoUpdateEnabled` が無いため、`Deserialize` のデフォルトで true になる（C# プロパティ初期化値）。OFF にしたいユーザーは情報タブで切替

