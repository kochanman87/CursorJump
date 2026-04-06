# CursorJump

## 概要
Windowsのカーソル操作ユーティリティ。タスクトレイに常駐し、グローバルショートカットでカーソルを操作する。

## 技術スタック
- .NET 8.0 (WPF + WinForms)
- C#, WinExe, PerMonitorV2 DPI対応
- 外部NuGetパッケージなし

## ビルド・実行
```bash
dotnet build src/CursorJump.App/CursorJump.App.csproj
dotnet run --project src/CursorJump.App/CursorJump.App.csproj
```

## プロジェクト構成
```
src/CursorJump.App/
├── App.xaml / App.xaml.cs          # エントリポイント、サービス初期化
├── MainWindow.xaml / .cs           # 不可視ウィンドウ（ホットキー受信+フック管理）
├── Models/
│   ├── AppSettings.cs              # 設定データモデル（ActionShortcut, ModifierKeyFlags等）
│   └── SavedCoordinate.cs          # 保存座標 record
├── NativeMethods.cs                # Win32 P/Invoke（ホットキー、マウスフック、カーソル等）
├── HotkeyService.cs                # RegisterHotKey APIでグローバルホットキー管理
├── MouseHookService.cs             # WH_MOUSE_LL低レベルフック（修飾キー+マウスクリック検知）
├── CursorService.cs                # カーソル移動（中央ジャンプ、任意座標ジャンプ）
├── CoordinateStore.cs              # 座標リスト管理（Add/GetNext循環/RemoveAt）
├── OverlayService.cs               # オーバーレイアニメーション（収縮円、軌跡、マーカー）
├── OverlayWindow.xaml / .cs        # 透明オーバーレイウィンドウ基盤
├── SettingsService.cs              # 設定のJSON読み書き（%APPDATA%/CursorJump/settings.json）
├── SettingsWindow.xaml / .cs       # 設定画面UI
├── TrayIconService.cs              # タスクトレイアイコン管理
├── app.manifest                    # DPI設定、実行レベル
└── CursorJump.App.csproj
```

## 機能一覧

### 中央ジャンプ（デフォルト: Ctrl+Alt+Home）
- カーソルを現在のモニター中央へ移動
- RegisterHotKey APIで実装

### 座標保存（デフォルト: Ctrl+Win+左クリック）
- クリック位置を座標リストに保存
- 赤い収縮円アニメーション表示

### 座標ナビゲーション（デフォルト: Ctrl+Win+右クリック）
- 保存した座標を順番に巡回ジャンプ
- 移動元→移動先に軌跡アニメーション（500msフェード）

### 座標表示/削除（デフォルト: Ctrl+Win+ホイールクリック）
- 全保存座標をマーカー表示
- マーカーをクリックで削除（吸いつき機能あり）
- Escで終了

### 設定画面（トレイアイコン右クリック→設定）
- 各アクションごとに独立した修飾キー+マウスボタンを設定可能
- アニメーション色のカスタマイズ
- 中央ジャンプのホットキー変更

## アーキテクチャ上の注意点
- **MainWindowは不可視**: Width=0, Height=0, Collapsed。HWNDメッセージ受信専用
- **ShutdownMode=OnExplicitShutdown**: ウィンドウクローズでアプリ終了しない
- **マウスフックのデリゲート**: `_hookProc`をフィールドに保持必須（GC回収防止）
- **UPイベント消費**: DOWNイベント消費時にフラグを立て、対応するUPイベントも消費する（右クリックメニュー抑止）
- **座標系**: マウスフック・SetCursorPosは物理ピクセル座標。WPFオーバーレイ描画時はTransformFromDeviceでDIP変換
- **設定ファイル**: `%APPDATA%/CursorJump/settings.json`（System.Text.Json）
