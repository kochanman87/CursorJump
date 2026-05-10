# CursorJump

Windows のカーソル操作ユーティリティ。タスクトレイに常駐し、グローバルショートカットで任意座標へカーソルをジャンプさせます。

## 機能

### 座標保存（デフォルト: Ctrl+Win+左クリック）
クリック位置を座標リストに保存します。保存時に収縮円アニメーションが表示されます。

### 座標ナビゲーション（デフォルト: Ctrl+Win+右クリック）
保存した座標を順番に巡回ジャンプします。移動時に軌跡アニメーションが表示されます。

### 座標表示/編集（デフォルト: Ctrl+Win+ホイールクリック）
保存済みの全座標をマーカーとして表示します。マーカー付近（40px 以内）の左クリックで削除、それ以外で新規追加。右クリック / Esc で終了。

### 第 2 座標セット（Set B、Pro 限定 / デフォルト: Win+Shift+左/右クリック）
Set A と完全独立した座標リストを保持・巡回できます。

### モニタ内ナビゲーション
現在カーソルのあるモニタ内の座標のみ循環するショートカット（デフォルト無効、設定画面で割当）。

### 設定画面（トレイアイコン右クリック → 設定）
- 各アクションの修飾キー + マウスボタンを自由にアサイン
- マウスボタンは左 / 右 / ホイール / 戻る(XButton1) / 進む(XButton2) に加え、ホイール+L/R の Chord、ホイール 2 連打 / 3 連打にも対応
- VIA キーボードカスタマイズツール経由の F13–F24 キーをトリガーに使用可能
- アニメーション色・軌跡の太さ / 持続時間 / 不透明度をカスタマイズ
- Light / Dark テーマ、日本語 / English 切替

### 自動更新
起動時に GitHub Releases をチェックし、新版があれば確認ダイアログで通知します。設定画面「情報」タブまたはトレイメニューから手動チェック・ON/OFF も可能。

## Free / Pro

無料版は座標保存 3 個まで、Set B は無効です。Pro 版は近日公開予定です。

## 動作環境
- Windows 10 / 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## ビルド・実行
```bash
dotnet build src/CursorJump.App/CursorJump.App.csproj
dotnet run --project src/CursorJump.App/CursorJump.App.csproj
```

設定ファイルは `%APPDATA%/CursorJump/settings.json` に保存されます。

## ライセンス
[MIT License](LICENSE)
