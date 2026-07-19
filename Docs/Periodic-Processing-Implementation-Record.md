# AunCast 定期処理改善 — 実装記録

作成日: 2026-07-19

## Phase 0 の静的確認結果

この記録はソースコードを確認した結果である。ClientSim、実機、長時間試験は未実施であり、
回数・遅延の実測値は記録していない。

| 計画項目 | 現行確認結果 | 対応 |
|---|---|---|
| 6.1 Update のマルチレート化 | Controller が監視・FSM・表示・無音検知を毎フレーム呼んでいた | PerFrame / Fast 0.1秒 / Slow 0.3秒を実装 |
| 6.2 RenderTexture | `UpdateRenderTexture` は毎フレーム呼ばれ、内部で同一テクスチャを抑制していた | Controller 側をダーティ駆動に変更。テクスチャ未到着時だけ再試行 |
| 6.3 Active/Standby 監視 | `AunCastActivePlayerMonitor` には `monitorIntervalSec = 0.1` の内部ゲートがあった | Controller からの呼び出しも Fast Tick に限定。Standby 側の時刻ゲートも更新 |
| 6.4 無音監視 | `Time.deltaTime` で毎フレーム積算。`AunCastSpeaker` は1024サンプル配列を再利用済み | RMS 取得を Fast Tick に限定し、実サンプル間隔で積算。バッファ再利用は維持 |
| 6.5 Coordinator | `_mySlotIndex` のキャッシュ、5秒再送、3秒の要求再送は既存 | 状態評価を Fast Tick、Force Reboot の保険確認を Slow Tick に配置 |
| 6.6 同期待ち | `_waitForSync` を Update で毎フレーム確認していた | `OnDeserialization` で即時評価し、Slow Tick に保険を残した |
| 6.7 スロット割当 | `_mySlotIndex` と5秒ゲートが既存 | 追加実装なし。Slow Tick から既存実装を呼ぶ |
| 6.8 Playback 報告 | 変化時 + 10秒キープアライブが既存 | 評価をダーティ化し、10秒送信は維持 |
| 6.9 UI | 各対象に Update/LateUpdate がある。HUD は非表示時に早期 return、Staff はイベント駆動 + 1秒フォールバック | 本変更の対象外。実測値を別途取得する |

## 変更後の呼び出し構造

```text
Update
├─ PerFrame: Crossfade 補間
├─ Fast (0.1秒): Coordinator / 同期待ち / Active・Standby監視 / FSM / 無音監視
├─ Slow (0.3秒): Global Force Reboot保険 / スロット割当・再送 / 同期待ち保険
├─ FlushVisualRouting: 状態変化時、またはテクスチャ未到着時のみ
└─ FlushPlaybackReport: 状態変化時、または10秒キープアライブ時のみ
```

## 計測方法

`AunCastDualPlayerController` の Timeline Logging を有効にすると、10秒ごとに
`[AunCast/Tick]` の集計ログを出力する。PerFrame/Fast/Slow、監視、RMS、表示適用、
Playback 評価・送信、Coordinator 評価の回数を比較に使用する。

UI 系は次の静的確認をした。実測は、Controller の通常再生60秒・パネル表示/非表示の各条件と
同時に取得する。

| Behaviour | 定期処理の確認結果 |
|---|---|
| `AunCastPortablePanel` | 毎フレームのジェスチャー入力と、0.5秒間隔の描画更新 |
| `AunCastWallControlPanel` | Crossfade 補間、距離判定、0.3秒ポーリング |
| `AunCastStaffControlPanel` | イベント駆動再描画、デバウンス、1秒フォールバック |
| `AunCastHudProgressOverlay` | 非表示かつ非フェード時は `LateUpdate` で早期 return |
| `AunCastAudioOutputTunnel` | A/B 音量を比較する毎フレーム Update |

## 未実施の検証

- ClientSim / Play Mode と2クライアント実機で、計画書のシナリオを実行する
- 30 / 60 / 90 FPS相当で無音判定時間を比較する
- 通常再生・手動/自動 Resync・Force Reboot・Late Join を含む2時間試験を行う
- Unity Editor の「参照関係を再配線」で `forceRebootNotifyTarget` を更新し、
  `AunCast/ResyncCoordinator` から `AunCast/DualPlayerController` の
  `AunCastDualPlayerController` へ配線されていることを確認する
- UdonSharp Program Asset を `Tools > UdonSharp > Refresh All UdonSharp Programs` で更新する
