# AunCast リファクタリング調査・実装計画

この計画書は、現在のコード挙動を正として、ドキュメント・コメント・設計上の不自然さを整理し、他のエージェントが実装に移れる粒度まで分解したものです。

> 更新: Phase 1 のドキュメント・コメント修正は 2026-07-09 に実施済み。Phase 2 の `AunCastSettingsInspector` partial 分割も同日に実施済み。以下の Phase 1 / Phase 2 は実施内容の記録として残す。
>
> 更新（第 2 ラウンド、2026-07-09）: 追加調査で発見した項目を実施済み。
> - ドキュメント修正: Design.md の旧設計残滓（FR-16b の非表示条件・長い Cooldown、18.3/21.1 の `Failed` 状態、`maxFailBeforeAlert`）、既定値の誤記（`localCooldownSec` 6.5s、`wallNearDistance` 2.8m、`silenceRmsThresholdDbfs`、`crossfadeDurationSec` の Settings 既定 0.1s）、音量カーブの帰属クラス（`GetAdjustedVolume` / `AunCastSpeaker.ApplyVolume`）、QA-Checklist のテスト名、Implementation-Patterns の見出し番号重複、release コマンドのファイル名誤記。
> - Phase 3 の項目 9（`MAX_PLAYERS` 重複）: `AunCastResyncCoordinator.MAX_PLAYERS` を `public const` にし、`AunCastPlaybackMonitor` から定数参照する形で統一（値は 82 のまま。要 `Refresh All UdonSharp Programs`）。
> - コード整理: URL 検証を `AunCastDualPlayerController.IsValidStreamUrl` に集約、未使用メソッド削除（`IsUnderTransform` / `ApplyThemeToAllProxies`）、`LoadAssetByGuid` を `AunCastEditorAssetUtility` へ集約、Migration.cs の汎用ヘルパーを `AunCastSettingsInspector.Common.cs` へ移動、スタッフパネルのインジケータ描画の配列使い回し化と到達不能分岐の削除、`GetAdjustedVolume` へのコメント追記。
> - 未使用 public メソッドの削除（ユーザー確認済み: マニュアル記載の `SetBaseVolume` 以外は外部呼び出しを想定しない）: `Reload` / `RestartOutputs` / `GetActiveResyncCount` / `GetAssignedUserCount` / `GetBaseVolume` / `GetLastRms` / `GetLastRmsSampleCount`（フィールド `_lastRmsSampleCount` ごと） / `GetCoordinator` / `IsResyncRequested` / `GetRequestStartedAt` / `GetSilenceSuppressSec` / `GetLastResyncCompletedAt` / `GetStallStartedAt` / `IsMenuVisible` / `OnPasscodeClear`。`OnMenuOpened` / `OnMenuClosed` は VRChat メニューイベントの受信口（`_isVRChatMenuOpen` 経由で表示制御に使用）のため存続。
> - `crossfadeDurationSec` はユーザー確認により 0.1 秒が正: `AunCastPlaybackSwitcher` の既定値を 0.3f → 0.1f に変更し、Design.md も 0.1 秒に統一。

## 前提

- コード挙動を正とする。ドキュメントはコード実態に合わせる。
- テスト・ビルド・lint は自動実行しない。必要な場合は事前にユーザーへ確認する。
- `Packages/tokyo.chigiri.pasocommate.auncast/` 配下を変更する場合は、`Docs/Design.md` と `Docs/Implementation-Patterns.md` の制約に従う。
- `Manual/` を変更する場合は `Manual/STYLE.md` に従い、原則として `Manual/src/` を編集対象にする。`Manual/site/` は生成物として扱う。
- UdonSharp の同期フィールド名、Prefab、`.asset` 参照に影響する変更は、互換性リスクが高いため別タスクとして扱う。
- 現在の作業ツリーには既存変更がある。実装前に `git diff` と対象ファイルの現状を確認し、既存変更を巻き戻さない。

## 優先順位

- P0: ドキュメントの破損・明確な矛盾。実装前に直す。
- P1: コメント、Tooltip、Class Diagram など、コード理解を誤らせる記述。
- P2: 挙動を変えない範囲の構造整理。
- P3: 効果測定や追加合意が必要な任意改善。

## Phase 1: ドキュメント・コメントのみの修正

### 1. `Docs/Design.md` の文字化けを修正する

対象:

- `Docs/Design.md`

確認済みの破損箇所:

- `状態テキス���表示`
- ``Pie モー��切替（`usePieMode`���``
- `` `RetryWait`（���系統失敗時）``
- `タ���ムアウト`

実装内容:

- UTF-8 として読み直しても破損しているため、表示環境ではなくファイル内容の問題として修正する。
- 想定される正しい語へ置換する。
  - `状態テキスト表示`
  - ``Pie モード切替（`usePieMode`）``
  - `` `RetryWait`（両系統失敗時）``
  - `タイムアウト`

検証:

- ユーザー許可がある場合のみ、`rg -n "�|���" Docs/Design.md` で残存確認する。

### 2. Silence Resync の説明を「個別 Resync」に合わせる

対象:

- `Docs/Design.md`
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastResyncCoordinatorClient.cs`

現在のコード挙動:

- 無音検知時は `REQUEST_REASON_SILENCE` による個別 Resync として扱われる。
- `AunCastPlaybackMonitor` の集計を使った global resync とは別系統。

矛盾:

- `Docs/Design.md` に「無音区間検知によるグローバル Resync」「Audio RMS used for global resync」のような記述が残っている。
- `AunCastResyncCoordinatorClient.cs` の Header が `Silence-Triggered Global Resync` になっている。

実装内容:

- Design の該当説明を「無音検知による個別 Resync」に更新する。
- Global Resync の説明では、多人数の再生失敗・接続失敗を監視する `AunCastPlaybackMonitor` 経由の経路だけを扱う。
- `AunCastResyncCoordinatorClient.cs` の Header を `Silence-Triggered Individual Resync` または `Silence Resync` に変更する。
- フィールド名は変更しない。

検証:

- ドキュメント上で `global` と `silence` が同じ機能として説明されていないことを確認する。

### 3. Portable Panel の呼び出しジェスチャ既定値をコードに合わせる

対象:

- `Docs/Design.md`
- 必要に応じて `Manual/src/`

現在のコード挙動:

- VR 既定値は右スティック上ホールド。
- Desktop 既定値は Tab ダブルタップ。
- `AunCastPortablePanel.summonGesture = GESTURE_RIGHT_STICK_UP_HOLD`
- `AunCastSettings.defaultSummonGesture = 2`
- `AunCastSettings.defaultDesktopSummonGesture = 1`

矛盾:

- Design に「ワンハンド対応のためダブルトリガーをデフォルト有効」と読める記述が残っている。

実装内容:

- 既定値の説明を、VR は右スティック上ホールド、Desktop は Tab ダブルタップへ修正する。
- ダブルトリガーは選択可能な呼び出し方法として説明する。
- PlayerData キーは既存の `AunCast-VrGesture` / `AunCast-DesktopGesture` のまま変更しない。

検証:

- `AunCastSettings` と `AunCastPortablePanel` の既定値に一致していることを読むだけで確認する。

### 4. 音量制御の責務説明を `AunCastSpeaker` 中心に直す

対象:

- `Docs/Design.md`
- `Docs/Class-Diagram.md`

現在のコード挙動:

- `AunCastVideoPlayerManager` はユーザー音量とフェードゲインを `AunCastSpeaker` に渡す。
- `AudioSource.volume` に最終値を書き込むのは `AunCastSpeaker.ApplyVolume()`。
- 最終音量は `baseVolume * adjustedUserVolume * fadeGain`。

矛盾:

- Design に `AunCastVideoPlayerManager` が `AudioSource.volume` を直接制御するような記述が残っている。
- `_localVolume` という現在存在しないフィールドへの言及がある。

実装内容:

- Manager は再生状態、URL、フェード、ユーザー音量の調整値を管理する役割として記述する。
- Speaker は AudioSource への最終反映責務を持つと記述する。
- `_localVolume` の記述は、現在の `_currentVolume` と PlayerData 永続化の説明に置き換える。

検証:

- `AunCastVideoPlayerManager` と `AunCastSpeaker` の責務説明が逆転していないことを確認する。

### 5. `Docs/Class-Diagram.md` を現在の API に合わせる

対象:

- `Docs/Class-Diagram.md`

確認済みの不一致:

- `AunCastEventBus.PublishVideoTexture(Texture)` は実際には `PublishVideoTexture(Texture tex, bool flipY)`。
- `AunCastVideoPlayerManager.GetAppliedOutputGain(AudioSource)` は現在存在しない。
- `AunCastAudioOutputTunnel` の `blockSamples`、`ringBufferSamples`、`RestartOutputs()` は現在の実装と合わない。
- `AunCastDualPlayerController` の同期フィールド一覧に `_syncedUrlSubmitterName` が不足している箇所がある。

実装内容:

- Diagram のメソッド、フィールド、公開 API を現行コードに合わせる。
- 実装を Diagram に合わせて変更しない。
- UdonSynced 一覧は `AunCastDualPlayerController` の実コードと照合する。

検証:

- 変更後に対象クラスの公開メンバー名を `rg` で照合する。ただしコマンド実行はユーザー許可後。

### 6. コードコメント・Tooltip の誤記を修正する

対象:

- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastActivePlayerMonitor.cs`
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/UI/AunCastPortablePanel.cs`
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/UI/AunCastStaffControlPanel.cs`

修正内容:

- `AunCastActivePlayerMonitor.cs`
  - `DetectActiveFailure` の呼び出し元コメントが `AunCastPlaybackSwitcher` になっているが、現在は `AunCastDualPlayerController` から呼ばれる。コメントを実態に合わせる。
- `AunCastPortablePanel.cs`
  - `headroomGauge` Tooltip が「ディレイバッファ残りゲージ」になっているが、現在は Drift 蓄積ゲージ。Tooltip を Drift Gauge として修正する。
- `AunCastStaffControlPanel.cs`
  - インジケータ優先度コメントが「赤エラー優先」と読めるが、実装では queued、running、connecting、error、normal の順で表示状態が決まる。コメントを現行コードに合わせる。

制約:

- Tooltip とコメントのみ変更し、SerializeField 名は変更しない。

検証:

- Unity 上の表示文言確認が必要な場合は、ユーザー許可後に実施する。

## Phase 2: 挙動を変えない構造整理

### 7. `AunCastSettingsInspector` を partial class に分割する

対象:

- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Editor/AunCastSettingsInspector.cs`
- 新規追加候補:
  - `AunCastSettingsInspector.Consent.cs`
  - `AunCastSettingsInspector.Rewire.cs`
  - `AunCastSettingsInspector.Migration.cs`
  - `AunCastSettingsInspector.SettingsApply.cs`
  - `AunCastSettingsInspector.VpmVersion.cs`
  - `AunCastSettingsInspector.UiSync.cs`

現在の問題:

- 1 ファイルに、利用規約同意、参照再配線、マイグレーション、検証、VPM バージョン確認、UI 同期、設定適用が集中している。
- Runtime や Udon 同期には直接関係しないため、分割による互換性リスクは比較的低い。

実装内容:

- 元クラスを `partial` にする。
- ロジックは移動のみとし、条件式や処理順を変えない。
- 既存メソッド名を維持する。
- 参照再配線、マイグレーション、VPM 確認などのまとまりでファイルを分ける。
- `#region` が多い場合は、分割後に不要なものだけ削除する。

注意:

- EditorWindow/Inspector のシリアライズ状態に関わるフィールドは、移動しても名前を変えない。
- `internal` / `private` の可視性変更は最小限にする。partial 内で共有できるものはそのまま `private` のままでよい。

検証:

- ユーザー許可後に Unity Editor コンパイル、または最低限 `git diff --check` を実行する。

### 8. Inspector の重い探索処理は測定してから最適化する

対象:

- `AunCastSettingsInspector` 系ファイル

現在の観察:

- `FindObjectsOfType<Component>(true)` などの広い探索が複数箇所にある。
- ただし Editor 操作時の処理であり、実測なしに最適化効果を断定できない。

実装方針:

- Phase 2 の分割では処理内容を変えない。
- 遅さが問題として再現できた場合に、Scene component index の一時キャッシュ化や migration pass 内の探索共有を検討する。

実装しないこと:

- 測定なしに探索条件や対象を狭めない。
- Migration の検出漏れにつながる高速化を入れない。

## Phase 3: 任意改善・別合意が必要な項目

### 9. `MAX_PLAYERS` の重複を整理する

対象:

- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastResyncCoordinator.cs`
- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastPlaybackMonitor.cs`

実装済み:

- `MAX_PLAYERS = 82` が複数箇所にあり、コメントで同期維持を求めている。

改善案:

- 低リスク案: `Docs/Implementation-Patterns.md` に「この値は両クラスで一致させる」と明記する。
- 中リスク案: Runtime constants クラスを追加し、両クラスから参照する。

注意:

- UdonSharp での const 参照、同期コード生成、アセット再コンパイルへの影響を確認する必要がある。
- 中リスク案はユーザー合意後に別タスク化する。

### 10. PlayerData の不正値正規化は別タスクにする

対象:

- `AunCastPortablePanel`
- `AunCastSettings`

現在の状態:

- Volume、VR Gesture、Desktop Gesture は PlayerData で永続化される。
- Silence Resync や Timeline Logging は永続化されていない。

判断:

- 追加の永続化は不要。ローカル診断・一時的な設定まで保存対象を広げると互換性と説明コストが増える。
- PlayerData に不正値や 0 が残った場合の正規化は改善余地があるが、挙動変更になるため別合意が必要。

実装しないこと:

- Silence Resync、Timeline Logging、Staff UI の一時表示状態を新たに永続化しない。
- 既存 PlayerData キー名を変更しない。

### 11. `PACKAGE_VERSION` を Runtime の単一の参照元にする

対象:

- `Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Udon/Core/AunCastDualPlayerController.cs`
- `Packages/tokyo.chigiri.pasocommate.auncast/package.json`
- 必要に応じてリリース手順ドキュメント

現在の状態:

- `AunCastDualPlayerController.PACKAGE_VERSION` を Runtime 側の単一の参照元とし、
  `AunCastWallControlPanel` はこれを参照する。
- Udon Runtime から `package.json` を読む設計にはしない方がよい。
- `.claude/commands/release.md` に、`package.json` と
  `AunCastDualPlayerController.PACKAGE_VERSION` を同時更新する手順を記載する。

今後の改善候補:

- 可能なら Editor 側の検証ボタンやチェック処理で不一致を警告する。

実装しないこと:

- Runtime でファイル IO して `package.json` を読む。
- 既存の `PACKAGE_VERSION` 表示挙動を急に削除する。

## 責務整理の結論

### 維持する責務

- `AunCastDualPlayerController`
  - URL 同期、FSM、Active/Standby 切替、ローカル UI 通知のオーケストレーター。
  - 既に `AunCastPlaybackSwitcher`、`AunCastActivePlayerMonitor`、`AunCastResyncCoordinator`、`AunCastPlaybackMonitor` に分担しているため、大きな分割は行わない。
- `AunCastVideoPlayerManager`
  - VideoPlayer/AVPro 操作、フェード制御、音量調整値の受け渡し。
- `AunCastSpeaker`
  - AudioSource への最終音量反映。
- `AunCastResyncCoordinator`
  - Resync 要求キュー、同時実行制限、Owner/Requester の状態管理。
- `AunCastPlaybackMonitor`
  - 全体傾向の集計と global resync 判定。
- `AunCastPortablePanel`
  - ローカル UI、呼び出しジェスチャ、PlayerData 永続化、Staff view 表示の統合 UI。
  - Udon コンポーネント分割は serialized reference のリスクが高いため、この計画では行わない。

### すぐには統合しない重複

- `AunCastScreen` と `AunCastUiScreen`
  - Texture 反映や idle 表示の似た処理はあるが、Renderer と RawImage で対象が異なる。
  - Udon での継承・共通化は見通しを悪くする可能性があるため、現状維持。
- Portable/Staff/Wall の Button interactable helper
  - 似た処理はあるが、CanvasGroup の扱いが微妙に異なる。
  - 横断 helper 化は急がない。

## 実装順序

1. Phase 1 のドキュメント破損と明確な矛盾を修正する。
2. Phase 1 のコードコメント・Tooltip を修正する。
3. 差分を確認し、Prefab や `.asset` に不要な変更が出ていないことを確認する。
4. ユーザー合意後に Phase 2 の `AunCastSettingsInspector` 分割へ進む。
5. Phase 3 は個別に合意を取ってから実装する。

## 実装担当へのチェックリスト

- 作業開始前に `CLAUDE.md`、`Docs/Design.md`、`Docs/Implementation-Patterns.md` を読む。
- `Manual/` を触る場合は `Manual/STYLE.md` を読む。
- 既存の未コミット変更を確認し、巻き戻さない。
- SerializeField、UdonSynced、Prefab 参照名は原則変更しない。
- コード挙動をドキュメントへ合わせる変更はしない。
- ドキュメントをコードへ合わせる。
- テスト・ビルド・lint・Unity Editor コンパイル確認は、ユーザー許可後に実行する。

## 推奨確認コマンド

以下は実行前にユーザー許可を取ること。

```powershell
rg -n "�|���" Docs/Design.md
git diff --check
git diff --stat
```

Udon 関連のコードを変更した場合は、Unity 上で UdonSharp の再コンパイルと対象 Prefab の参照確認を行う。
