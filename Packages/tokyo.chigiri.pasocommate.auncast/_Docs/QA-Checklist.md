# AunCast QA チェックリスト

> バージョン: 2.0.0  
> 対象: AunCast（VRChatワールド向けライブ配信ビデオプレイヤー）  
> 更新日: 2026-05-06

---

## Part 1: 自動テスト（Unity Test Runner）

Unity Editor で `Window > General > Test Runner > EditMode` から実行する。
テストコード: `Packages/tokyo.chigiri.pasocommate.auncast/_Tests/Editor/`

### Setup 系

| テスト | 検証内容 |
|--------|---------|
| `AllUdonScriptsHaveAsset` | 全 UdonSharp .cs に対応する .asset が存在する |
| `AllAssets_HaveMatchingScript` | 孤立 .asset がない |
| `SetupScene_CompletesWithoutError` | Setup 実行がエラーなく完了する |
| `SetupScene_AllCoreComponentsExist` | 必須コンポーネントが全て配置される |
| `SetupScene_AllImagesHavePositionAsUV1` | 全 Image に PositionAsUV1 が付与されている |
| `MonitorInterval_NotExceedRecommended` | monitorIntervalSec ≤ 0.1 |
| `StalledTimeout_InRange` | stalledTimeoutSec ∈ [1.5, 3.0] |
| `MaxConcurrent_SafeForCDN` | maxConcurrentResyncUsers ≤ 15 |
| `CycleTimeout_LessThanRunningTimeout` | resyncCycleTimeoutSec < runningTimeoutSec |
| `DriftThreshold_IsConfigured` | driftResyncThresholdSec > 0 |

### Logic 系

| テスト | 検証内容 |
|--------|---------|
| `RoundTrip_PreservesWithin100ms` | タイムスタンプ圧縮の往復精度が 0.1 秒以内 |
| `AllNone_OffsetIsZero` | 全スロット空の場合 offset=0 |
| `SelectNextQueuedUser_ReturnsOldestTimestamp` | FIFO: 最古を選択 |
| `SelectNextQueuedUser_NoQueued_ReturnsNegative` | キューなし → -1 |
| `SelectNextQueuedUser_IgnoresGrantedAndRunning` | QUEUED のみ対象 |
| `CountGrantedOrRunning_MixedStates` | 正確なカウント |
| `GrantedTimeout_ClearsSlot` | Grant タイムアウトでスロット解放 |
| `RunningTimeout_ClearsSlot` | Running タイムアウトでスロット解放 |
| `NotExpired_NoChange` | タイムアウト前は維持 |
| `NegativeElapsed_Skipped` | 時刻巻き戻り保護 |
| `SetAndGet_CorrectBit` | ビット操作の正確性 |
| `CountBits_SparsePattern_CorrectCount` | popcount の正確性 |
| `ClearSlot_ClearsAllArrays` | 3 配列を同時クリア |
| `AfterWarmup_EmaConvergesToRawDrift` | ドリフト EMA の収束 |
| `Alpha_Formula_IsCorrect` | EMA 係数の数式検証 |
| `DetectActiveFailure_StallTimeout_True` | 停滞タイムアウトで障害検出 |
| `DetectActiveFailure_DriftOverThreshold_True` | ドリフト超過で障害検出 |
| `DetectActiveFailure_Normal_False` | 正常時は検出しない |
| `DetectActiveFailure_DuringWarmup_IgnoresDrift` | ウォームアップ中はドリフト無視 |
| `IsVerifySatisfied_EnoughAdvances_True` | Standby 検証の合格条件 |
| `IsVerifySatisfied_NotEnoughAdvances_False` | カウント不足 |
| `IsVerifySatisfied_NotEnoughTime_False` | 時間不足 |
| `WithinSuppressSec_NotEligible` | 無音 Resync 抑制期間中 |
| `AfterSuppressSec_Eligible` | 抑制期間後は適格 |

---

## Part 2: 手動テストシナリオ

各シナリオを実施することで、関連する複数の検証ポイントをまとめてカバーする。

| 記号 | 意味 |
|------|------|
| ✅ | 合格 |
| ❌ | 失敗（Issue 起票） |
| ⏭️ | スキップ（理由明記） |

---

### シナリオ A: 基本再生 + スタッフ操作

**環境:** ClientSim（1人、スタッフアカウント）

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| A1 | StaffPanel で HLS URL を入力し再生ボタン押下 | 5 秒以内に映像・音声が出力される | | |
| A2 | Tab キーを繰り返し押す | Viewer → Staff → 非表示のサイクルで切り替わる | | |
| A3 | 停止 → 別 URL → 再生を 3 回繰り返す | 毎回正常に遷移し、停止中に Resync が発火しない | | |
| A4 | 音量スライダーを 0 → 最大 → 中間で操作 | ローカルのみ音量変化、ミュート/復帰正常 | | |
| A5 | 再生中に StaffPanel のステータス表示を確認 | 状態・ドリフト値がリアルタイム更新される | | |
| A6 | 非スタッフアカウントで StaffPanel を操作しようとする | 操作不可（パスコード要求） | | |

---

### シナリオ B: 自動Resync + A/B切替

**環境:** ClientSim（1人）＋ 低品質/一時停止可能なストリーム

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| B1 | 正常ストリームで再生開始後 5 秒間ログ監視 | driftWarmupSec 中に Resync が発火しない | | |
| B2 | ネットワーク一時遮断またはバッファリング誘発 | stalledTimeoutSec 後に REQUEST_PENDING へ遷移 | | |
| B3 | Resync フロー全体をログ追跡 | ACTIVE→REQUEST_PENDING→RESERVED→STANDBY_CONNECTING→VERIFYING→SWITCHING→COOLDOWN の順に遷移 | | |
| B4 | 切替時に耳で音声確認 | クロスフェードが自然（プツ切れなし） | | |
| B5 | 切替後にログ確認 | 旧 Active が Standby 降格、COOLDOWN 発動 | | |
| B6 | 無効 URL に変更して両プレイヤー失敗を誘発 | RETRY_WAIT 遷移、指数バックオフで再試行 | | |
| B7 | B3 の Resync 中に別のストール発生 | 二重発火しない（1 回のみ処理） | | |

---

### シナリオ C: 手動Resync + グローバルResync + CDN制限

**環境:** ClientSim（1人）

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| C1 | ViewerPanel の Resync ボタンを押す | 自分のみ Resync される | | |
| C2 | クールダウン中に再度ボタン押下 | ボタン無効、残り時間表示あり | | |
| C3 | StaffPanel のグローバル Resync ボタンを押す | Coordinator が全スロットをキューイング | | |
| C4 | maxConcurrentResyncUsers を 1 に変更 | 順次処理（同時 1 件のみ GRANTED/RUNNING） | | |
| C5 | Resync 完了後にスロット状態確認 | 即座に STATE_NONE に戻る | | |

---

### シナリオ D: VRパネル操作

**環境:** VRChat Build & Test（VR HMD）

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| D1 | 右スティック上をホールド | HUD プログレスリングが視界に表示され、完了後パネル召喚（ディゾルブ演出） | | |
| D2 | パネルを Grab で移動 | 手の動きに追従 | | |
| D3 | パネル表示中に再度召喚 | 頭部前方に再配置 | | |
| D4 | Viewer/Staff ビューを切り替え | CanvasGroup フェードでスムーズに切替 | | |
| D5 | 各ボタン・スライダーにホバー | ツールチップが日本語/英語で正しく表示 | | |
| D6 | WallControlPanel でジェスチャー設定トグルを変更 | 選択したジェスチャーのみ有効化 | | |

---

### シナリオ E: マルチクライアント同期

**環境:** VRChat 3人以上（スタッフ 2 + 一般 1 以上）

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| E1 | スタッフ A がストリーム開始 | 全員の画面に映像が映る | | |
| E2 | 一般ユーザーが後から参加 | URL 自動受信、自動再生、driftWarmup 中 Resync なし | | |
| E3 | スタッフ A が退出 | Owner が引き継がれ、スケジューラ・Resync が継続 | | |
| E4 | スタッフ B が URL を変更 | 全員に新 URL が反映される | | |
| E5 | スタッフ A（復帰後）と B が同時に操作 | 競合なし、最後の変更が適用 | | |
| E6 | 非スタッフが管理コマンドを試行 | 弾かれる | | |
| E7 | 2人以上が同時退出 | 残存者に影響なし | | |
| E8 | リスポーン操作 | 再生状態が維持される | | |

---

### シナリオ F: エラーハンドリング

**環境:** ClientSim or VRChat（1-2人）

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| F1 | 無効 URL を入力して再生 | エラー表示、指数バックオフで再試行 | | |
| F2 | 空 URL で再生ボタン押下 | 再生開始しない | | |
| F3 | 256 文字の URL を入力 | UI 崩れなし | | |
| F4 | 正常再生中に CDN 切断を模擬 | タイムアウト後に再試行される | | |
| F5 | Coordinator スロットを満杯にして新規リクエスト | キュー待機に入る | | |
| F6 | 両プレイヤー失敗を誘発 | RetryWait 状態に遷移、UI にエラー反映 | | |

---

### シナリオ G: 長時間安定性

**環境:** VRChat 実機 + Unity Profiler（60 分放置）

| # | 確認ポイント | 合格基準 | 結果 | 備考 |
|---|-------------|---------|------|------|
| G1 | クラッシュ・フリーズなし | 60 分間安定動作 | | |
| G2 | FPS 低下 | < 10%（ベースライン比） | | |
| G3 | メモリ増加 | < 50 MB | | |
| G4 | 自動 Resync 回数 | < 3 回/時間 | | |
| G5 | 映像・音声の品質 | 正常出力継続 | | |
| G6 | GetOutputData による CPU 負荷 | 増加 < 5% | | |
| G7 | アイドル時のネットワーク送信 | 不要パケットなし | | |

---

### シナリオ H: 大人数 + 無音検知

**環境:** VRChat 20人以上

| # | 手順 | 確認ポイント | 結果 | 備考 |
|---|------|-------------|------|------|
| H1 | 20人以上接続後グローバル Resync 実行 | CDN 同時接続上限が守られる | | |
| H2 | 無音ストリームに切替 | silenceConsecutiveSec (2秒) 後に Resync 発火 | | |
| H3 | H2 の Resync 後 150 秒以内に再度無音化 | 再発火しない（silenceSuppressSec 抑制） | | |
| H4 | maxConcurrentResyncUsers = 1 に設定 | 全員が順次処理される | | |
| H5 | 1 人のみのインスタンスで正常動作 | Owner のみでもエラーなし | | |
| H6 | 80 人規模で Coordinator 動作確認 | スロット管理が正確 | | |

---

## Part 3: リリース判定基準

| 判定 | 条件 |
|------|------|
| **リリース可** | 自動テスト全件 Pass、手動シナリオ ❌ が 0 件 |
| **条件付きリリース可** | ❌ が P3 以下のみで全件 Issue 起票済み |
| **リリース不可** | ❌ が P1 または P2 の案件が 1 件以上存在 |

### 優先度定義

| 優先度 | 定義 | 例 |
|--------|------|-----|
| P1 | ユーザー全員に影響、サービス停止レベル | 再生不可、クラッシュ |
| P2 | 特定条件で機能不全、回避策なし | Resync 不動作、Owner変更でフリーズ |
| P3 | 一部ユーザーへの影響、回避策あり | 特定UIの表示ズレ |
| P4 | 軽微な問題 | ツールチップの誤字 |

---

## 付録：テスト環境

### 必須

- VRChat クライアント（最新版）
- テスト用 HLS ストリームサーバー（VRCDN または同等）
- スタッフアカウント × 2
- 一般アカウント × 1 以上
- VRChat ClientSim

### 推奨

- 負荷テスト用アカウント × 20 以上
- ネットワーク遅延シミュレーター
- Unity Profiler

### テスト用ストリーム

| 種別 | 用途 |
|------|------|
| 通常 HLS ストリーム | シナリオ A, C, E |
| 無音ストリーム | シナリオ H |
| 低品質ストリーム（バッファリング発生） | シナリオ B |
| 無効 URL | シナリオ F |
