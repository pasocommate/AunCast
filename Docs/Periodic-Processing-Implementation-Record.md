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
├─ FlushVisualRouting: 状態変化時のみ（テクスチャ未到着時は Fast Tick で再試行）
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

## レビュー指摘による修正

初回実装のレビューで以下を修正した。いずれも実機未検証。

| 指摘 | 内容 | 対応 |
|---|---|---|
| EventBus 通知の遅延 | `NotifyLocalStateChangeIfNeeded` が `TickFast` 内のみになり、`RequestImmediateFastTick` を伴わない `TryManualResync` / `PlayVideo` で `LocalStateChanged` が最大100ms遅延 | `Update` 末尾へ戻した |
| 映像再試行の毎フレーム化 | テクスチャ未到着中は `_visualRoutingDirty` が立ち続け、Active リブート待ちなど異常時に毎フレーム呼び出しへ戻る | `_visualRoutingRetryPending` に分離し `TickFast` で再試行 |
| Crossfade 中のダーティ握り潰し | `UpdateRenderTexture` が `_crossfading` 中に `false` を返し、ダーティ要求が失われる | 再試行要求として `true` を返す |
| Crossfade 中断の未復帰（既存不具合） | `_crossfading` は `CompleteSwitchRoles` でしかクリアされず、サイクルタイムアウト等の中断で映像固着と Active ゲイン低下が残る | `StopStandbyOnFailure` でクリアし Active を全開へ戻す |
| 同期受信のダーティ漏れ | `_ownerPlaying` は同期受信が直接書き換えるため `SetOwnerPlaying` を通らない | `ApplySyncedState` 冒頭で無条件にダーティ化 |

## 完了済みの反映

- `AunCast.prefab` の `AunCastResyncCoordinator` へ `forceRebootNotifyTarget` を配線済み
- UdonSharp Program Asset (`AunCastDualPlayerController` / `AunCastActivePlayerMonitor` /
  `AunCastResyncCoordinator`) を再生成済み

## 未実施の検証

- **上記「レビュー指摘による修正」を反映した Program Asset の再生成**
  （`Tools > UdonSharp > Refresh All UdonSharp Programs`）
- ClientSim / Play Mode と2クライアント実機で、計画書のシナリオを実行する
- 30 / 60 / 90 FPS相当で無音判定時間を比較する
- 通常再生・手動/自動 Resync・Force Reboot・Late Join を含む2時間試験を行う
- ユーザーのシーン側は「参照関係を再配線」で `forceRebootNotifyTarget` の
  更新が必要（`AunCast/ResyncCoordinator` → `AunCast/DualPlayerController`）
- Crossfade 中断時のゲイン復帰は、サイクルタイムアウト (45s) を意図的に
  起こす必要があり再現手順が未確立
