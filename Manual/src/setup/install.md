# 導入とパラメータ設定（ワールド設置者向け）

このページは、自分の VRChat ワールドに AunCast を組み込む制作者向けの導入手順と、調整できるパラメータの早見表です。実装の設計背景は[Docs/Design.md](https://github.com/pasocommate/AunCast/blob/main/Docs/Design.md)を参照してください。

!!! warning "このページは概要です"
    導入の細かな手順（prefab の具体的な配置位置など）はパッケージのバージョンにより変わることがあります。**実際のメニュー名やボタン位置は同梱の最新情報を優先** してください。不明点を憶測で進めず、リポジトリの設計資料も併せて確認することをおすすめします。

---

## 動作環境と依存

| 項目 | 値 |
|---|---|
| Unity | 2022.3 |
| VRChat SDK | `com.vrchat.worlds >= 3.10.2` |
| フォント | `net.narazaka.vrchat.tmp-fallback-fonts-jp >= 1.0.0` |
| 描画 | `tokyo.chigiri.pasocommate.rendermate >= 3.0.0 < 4.0.0` |

これらは VPM（VRChat Package Manager）の依存として解決されます。

---

## 導入の流れ（概要）

1. VPM 経由で AunCast パッケージ（`tokyo.chigiri.pasocommate.auncast`）をプロジェクトに追加する。
2. シーンに **`AunCast.prefab`** を配置する（再生・再同期の本体一式）。
3. 必要に応じて **`WallControlPanel.prefab`**（壁掛け操作パネル）を設置する。
4. スクリーンを自分のワールドに合わせて配置する（下記）。
5. `AunCastSettings` でパラメータと上限を設定する（下記）。
6. 検証用シーンやローカル RTSP で動作確認する（下記）。

!!! tip "検証用シーン"
    リポジトリには検証用シーン `Assets/AunCast-Dev/AunCast-Verify.unity` が含まれています。導入後の挙動確認の参考になります。

---

## スクリーンの配置

映像の出力先（スクリーン）は、ワールドに合わせて自由に配置できます。

- **3D スクリーン**: 対象の GameObject に `VideoMeshScreen` を付ける。
- **UI スクリーン（RawImage）**: 対象に `VideoUiScreen` を付ける。

映像は内部のイベントハブ（`AunCastEventBus`）経由で配信されるため、**スクリーンを何枚増やしても配信負荷はほぼ一定** です。スクリーンを追加したら、`AunCastSettings` の「`AunCastEventBus` 参照を再配線」を実行して購読者を繋ぎ直します。

!!! note "アイドル画像"
    停止中の表示は `AunCastSettings.idleScreenTexture` で指定できます。未指定の場合は各スクリーンの初期テクスチャに戻ります。停止時に白飛び（null テクスチャ）にならないよう設定しておくことを推奨します。

---

## 接続上限の設定（最重要）

配信サーバー（CDN）の同時接続上限に合わせた設定は、AunCast 運用の要です。考え方は[同時接続上限の管理](../concepts/connection-limit.md)を必ず読んでください。

| パラメータ | 設定方針 |
|---|---|
| `maxConnectionLimit`（CDN 総接続数上限） | 契約している **CDN プランの同時接続数** を入れる（例: 100） |
| `maxConcurrentResyncUsers`（同時 Resync 実行数上限） | 想定インスタンス人数での空き枠から、安全マージンを引いた値（**10〜15** が出発点） |

どちらも **スタッフがワールド内で実行中に変更可能** です。設置時には妥当な初期値を入れておき、本番ではスタッフが状況を見て調整します（[モニタリングと上限調整](../staff/monitoring.md#limits)）。

---

## 主要パラメータ早見表

チューニングパラメータは `AunCastSettings`（Editor 専用）に集約されており、ここから一括編集できます。内部的には各コンポーネントに分散配置されています。

### 異常検知・前進判定（ActivePlayerMonitor）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `monitorIntervalSec` | 0.1 秒 | `GetTime()` の観測間隔 |
| `minAdvanceThresholdSec` | 0.01 秒 | 前進とみなす最小量（float ノイズ除去） |
| `minConsecutiveAdvances` | 5 回 | 生存とみなす連続前進回数（≒0.5 秒） |
| `stalledTimeoutSec` | 2.0 秒 | この時間前進しなければ停止と判定 |
| `verifyMinDurationSec` | — | 切替前の検証最小時間 |

### ドリフト検知（ActivePlayerMonitor）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `driftResyncThresholdSec` | 0.1 秒 | これを超えると再同期を要求 |
| `driftSmoothingTimeConstant` | 1.5 秒 | ドリフト平滑化の時定数 |
| `driftWarmupSec` | 5.0 秒 | 再生開始直後はドリフト判定しない猶予 |

### 切替（PlaybackSwitcher）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `crossfadeDurationSec` | 0.3 秒 | 音声クロスフェードの長さ（推奨 0.3〜0.5） |

### 再同期サイクル・クールダウン（ResyncCoordinatorClient）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `resyncCycleTimeoutSec` | 45 秒 | 許可〜切替完了の全体制限 |
| `silenceSuppressSec` | 150 秒 | 再同期後に無音検知を止める時間 |
| `localCooldownSec` / `baseCooldownSec` | 5 秒 | 再同期後の再要求抑止 |
| `retryCooldownMultiplier` | 1.5 | 連続失敗時のバックオフ倍率 |
| `maxRetryCooldownSec` | 90 秒 | バックオフの上限 |

### タイムアウト・スロット（ResyncCoordinator）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `maxConcurrentResyncUsers` | 同期・可変 | 同時再同期人数（上記参照） |
| `maxConnectionLimit` | 同期・可変 | CDN 総接続数上限（上記参照） |
| `grantTimeoutSec` | 10 秒 | 許可後に開始報告がなければ解放 |
| `runningTimeoutSec` | 50 秒 | 実行後に結果報告がなければ解放 |
| `MAX_PLAYERS` | 82 | 同期スロット配列の固定長（Group+ 上限） |

### 無音検知（AudioSilenceDetector）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `silenceRmsThreshold` | 0.001 | 無音とみなす音量しきい値 |
| `silenceConsecutiveSec` | 2.0 秒 | 無音が続いたら検知 |

### 壁掛けパネルの距離切替（WallControlPanel）

| パラメータ | 既定 | 意味 |
|---|---:|---|
| `wallNearDistance` | 2.5 m | これより近いと詳細表示 |
| `wallFarDistance` | 3 m | これより遠いと大型 Resync 表示 |

!!! note "推奨値は出発点"
    上記の既定値は設計上の推奨値であり、実機での長時間視聴テスト（30 / 60 / 120 分）を通じて調整する前提です。配信の特性に合わせて見直してください。

---

## アクセス制御の設定

スタッフ操作画面を使えるユーザーを制限します。

- **ユーザー名リスト**（`allowedUserNames`）: Inspector に登録したユーザーは解錠なしで Staff ビューを使えます。
- **パスコード解錠**: 壁掛けパネルの 4 桁パスコードで、各クライアントがローカルに解錠できます（同期されません）。

両方を併用できます。運営メンバーは名前で登録し、当日ヘルプには口頭でパスコードを伝える、といった運用が可能です。

---

## 動作確認（ローカル RTSP）

実際の配信サーバーを使わずに、ローカルで `rtsp://` / `rtspt://` ストリームを立てて検証できます。MediaMTX + FFmpeg を使う手順がリポジトリにまとまっています。

- [Docs/Local-Test-Server.md](https://github.com/pasocommate/AunCast/blob/main/Docs/Local-Test-Server.md) — ローカル RTSP 検証手順
- [Docs/QA-Checklist.md](https://github.com/pasocommate/AunCast/blob/main/Docs/QA-Checklist.md) — 検証観点

!!! warning "ビルド・テストの実行について"
    シーンの再生・ビルド・テスト実行は、ワールドの状態に影響します。本マニュアルでは手順の説明にとどめ、実行はワールド制作者の判断で行ってください。
