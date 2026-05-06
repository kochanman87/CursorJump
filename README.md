# CursorJump

Windowsのカーソル操作ユーティリティ。タスクトレイに常駐し、グローバルショートカットでカーソル位置の保存・巡回ジャンプを行います。

## 機能

### 座標保存（デフォルト: Ctrl+Win+左クリック）

クリック位置を座標リストに保存します。保存時に収縮円アニメーションが表示されます。

### 座標ナビゲーション（デフォルト: Ctrl+Win+右クリック）

保存した座標を順番に巡回ジャンプします。移動時に軌跡アニメーションが表示されます。

### 座標表示/編集（デフォルト: Ctrl+Win+ホイールクリック）

保存済みの全座標をマーカーとして画面上に表示します。

- 左クリック: マーカー付近（40px以内）→ 削除、それ以外 → 新規追加
- 右クリック / Esc: モード終了

座標が0個の状態でも表示モードに入れるため、追加専用としても使えます。

### 設定画面（トレイアイコン右クリック → 設定）

- 各アクションごとに修飾キー + マウスボタンを自由に設定可能
- マウスボタンの選択肢: 左 / 右 / ホイール / 戻る(XButton1) / 進む(XButton2)
- 戻る/進むボタンは修飾キーなしで単体割り当て可能
- 左/右/ホイールは修飾キーが1つ以上必要（誤動作防止）
- アニメーション色のカスタマイズ（カラーピッカーで選択）

### 自動更新

GitHub Releases に新版がリリースされるとアプリ起動時に検出し、リリースノートとともに更新ダイアログを表示します（[Velopack](https://github.com/velopack/velopack) 利用）。「今すぐ更新」を選択するとバックグラウンドでダウンロード後、自動的に再起動して新版に切り替わります。

- 設定画面の「情報」タブで自動更新の ON/OFF 切替、バージョン表示、手動チェックが可能
- トレイメニューの「更新を確認」からも手動チェックできます
- 「このバージョンをスキップ」を選ぶと、当該バージョンは起動時通知から除外されます

## 動作環境

- Windows 10 / 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## ビルド・実行

```bash
dotnet build src/CursorJump.App/CursorJump.App.csproj
dotnet run --project src/CursorJump.App/CursorJump.App.csproj
```

## 使い方

1. ビルド後、アプリを起動するとタスクトレイにアイコンが表示されます
2. 上記のショートカットでカーソル操作を行います
3. トレイアイコンを右クリックして「設定」からショートカットや色をカスタマイズできます
4. トレイアイコンを右クリックして「終了」でアプリを終了します

設定ファイルは `%APPDATA%/CursorJump/settings.json` に保存されます。

## リリース手順（メンテナ向け）

GitHub Releases へ自動更新可能なパッケージを公開する手順:

```bash
# 1. csproj の <Version> を更新（例: 1.3.0）してコミット
# 2. Velopack CLI を導入（初回のみ）
dotnet tool install -g vpk

# 3. self-contained ビルド
dotnet publish src/CursorJump.App/CursorJump.App.csproj -c Release -r win-x64 --self-contained -o publish/

# 4. Velopack パッケージング
vpk pack --packId CursorJump --packVersion 1.3.0 --packDir publish/ --mainExe CursorJump.App.exe
```

`Releases/Setup.exe`、`*-full.nupkg`、`RELEASES` を GitHub Releases にアップロードします（タグ `v1.3.0`）。リリースノートの Markdown が更新ダイアログにそのまま表示されます。

## ライセンス

[MIT License](LICENSE)
