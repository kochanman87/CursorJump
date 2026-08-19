# 調査メモ: ドック着脱でジャンプ先が別モニタにずれる

- 状態: **実装済み / 実機未検証**（2026-08-19 時点）
- 対象バージョン: v1.9.2
- 修正バージョン: v1.9.3（バグ修正 → PATCH）
- 実装差分: `MonitorIdentity.cs` / `JumpTargetResolver.cs` 新規、`SavedCoordinate` に
  `MonitorKey` + `MonitorFingerprint` 追加、`CoordinateStore` / `MonitorFilter` /
  `OverlayService.BuildMarkers` / `MainWindow` をキー基準へ。単体テストは
  `JumpTargetResolverTests` / `MonitorFilterTests` / `CoordinateStoreTests`（計 67 件パス）
- 残: ドック着脱の実機確認（下記「検証」の手順）

## 症状

ノートPC本体 + USB-C ドック経由の外部モニタ2枚（合計3画面）で座標を保存し、
USB-C を抜いて（ノート1枚に戻る）から再接続すると、保存したジャンプ箇所が
**1枚隣のモニタに飛ぶ**。

## 証拠（再現時の debug.log）

`NavigateA/B before:` 行の値を突き合わせると次の対応になっていた。

| 保存されたモニタ名 | stored | rel | jump | 保存時の Bounds.Left | 再接続後の Bounds.Left |
|---|---|---|---|---|---|
| `\.\DISPLAY1` | (2877,551) | (957,551)  | (957,551)  | 1920 | 0    |
| `\.\DISPLAY2` | (4849,512) | (1009,512) | (2929,512) | 3840 | 1920 |
| `\.\DISPLAY3` | (913,548)  | (913,548)  | (4753,548) | 0    | 3840 |

3枚が 1 つずつ巡回している。ユーザーがディスプレイ設定で並べ替えた場合に
この綺麗なローテーションが起きる可能性は低く、**Windows が `\.\DISPLAYn`
というデバイス名を物理モニタに振り直した**ことを示す指紋と判断した。

## 原因

保存座標 (`SavedCoordinate`) は物理モニタの identity として GDI のデバイス名
`\.\DISPLAYn` (`Screen.DeviceName`) を使っている。この名前はセッション内の
アダプタ出力の並び順に過ぎず、ディスプレイの着脱・ドック再接続で振り直される。

`MainWindow.ResolveJumpTarget` は「保存されたデバイス名のモニタの現 Bounds +
保存された相対座標」で絶対座標を再計算するため、名前と物理モニタの対応が
ずれた瞬間に**相対座標が別のモニタへ適用される**。

### 派生バグ

削除モードのマーカー描画 (`OverlayService.BuildMarkers`) は解決後の座標ではなく
`coord.X / coord.Y`（保存時の絶対座標）をそのまま描いている。よって名前の
振り直しが起きると「マーカーの見える位置」と「実際のジャンプ先」も食い違う。

## 修正方針

`\.\DISPLAYn` に代えて、物理モニタに紐づく**安定キー**を保存し照合する。
取得は `EnumDisplayDevices(deviceName, 0, ..., EDD_GET_DEVICE_INTERFACE_NAME)`
が返すモニタのデバイスインターフェースパス
（`\?\DISPLAY#<EDID製造元/型番>#<インスタンス&UID>#{GUID}`）。
EDID 由来のハードウェア ID と出力ポート UID を含むため、同型番2枚でも区別でき、
同じドックの同じポートに戻せば同じ値になる。

## 実装ステップ

1. **`MonitorIdentity.cs`（新規）**: `Snapshot()` が各 `Screen` の
   `(GdiDeviceName, StableKey, FriendlyName, Bounds)` を返す。P/Invoke は
   `EnumDisplayDevices` を `NativeMethods.cs` に追加。取得失敗時はキー空文字で
   従来動作にフォールバック（例外は `DebugLog` に記録して握りつぶす）。
   `SystemEvents.DisplaySettingsChanged` でキャッシュ無効化。
2. **`SavedCoordinate` に `MonitorKey` を追加**（record 末尾パラメータ、既定 `""`
   ＝旧 settings.json 互換）。
3. **`JumpTargetResolver.cs`（新規・純粋関数）**: 現 `ResolveJumpTarget` を
   モニタスナップショットを引数に取る形へ切り出してテスト可能にし、照合を多段化。
   1. `MonitorKey` 一致
   2. （キー無し旧データ）フレンドリ名 + 解像度が**一意に**一致するモニタ
   3. `DeviceName` 一致（従来動作）
   4. 絶対座標フォールバック
   最後に解決結果をそのモニタの Bounds 内へクランプする。
4. **`CoordinateStore`**: `Add` でキーを記録。`Load` の既存マイグレーション機構に
   「キー空 → 現在の DeviceName からキー補完」を追加（既存 `migrated` フラグ経路で
   settings.json へ書き戻る）。`GetNextInMonitor` / `GetPrevInMonitor` /
   `_monitorIndices` のグルーピングをキー基準へ。
5. **`MonitorFilter`**: 接続判定をキー優先（キー無しは名前で判定）に変更。
6. **`OverlayService.BuildMarkers`**: マーカー位置を `JumpTargetResolver` の
   解決結果で描画し、ジャンプ先と一致させる。
7. **診断ログ強化**: 起動時と `DisplaySettingsChanged` 時に
   「DeviceName ↔ 安定キー ↔ フレンドリ名 ↔ Bounds ↔ DPI」の対応表を出力。
   ナビゲートログに `matchedBy=key|fingerprint|name|absolute` を追加。
8. **テスト** (`tests/CursorJump.Tests/`): `JumpTargetResolverTests`
   （デバイス名が巡回入れ替わりしたスナップショットでキー照合が正しいモニタへ
   解決される / 旧データが名前照合へ落ちる / クランプ）、`MonitorFilterTests` 拡張、
   `CoordinateStoreTests` にキー補完マイグレーション。
9. **バージョン更新**: `<Version>` を 1.9.3 へ。CLAUDE.md の該当節
   （座標保存形式・MonitorFilter）も同時更新。

## 検証

根本の再現はドック着脱環境でしか起こせないため、開発機でできるのは単体テストと
ログ設計まで。実機手順は「修正版を入れる → 3画面で座標保存 → USB-C 抜く →
挿す → ジャンプ」。ステップ 7 のログがあれば、仮に直っていなくても
「キー自体が変わったのか、照合が外れたのか」をログだけで切り分けられる。

## 既知のリスク

- USB-C のポートやドックを変えると UID が変わり得る。その場合はステップ 3 の
  第2段（フレンドリ名 + 解像度）が受け皿になり、それも外れれば従来動作へ落ちるだけで
  現状より悪化はしない。
- マイグレーション（ステップ 4）は「実行時点の DeviceName ↔ 物理モニタ対応」を
  正としてキーを埋めるため、既に振り直しが起きている状態で初回起動すると
  誤ったキーが埋まる。その場合は座標を保存し直せば解消する。
