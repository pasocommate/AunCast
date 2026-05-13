# AunCast

AunCast は、VRChat ワールド向けの低遅延ライブ配信プレイヤーシステムです。  
`VRCAVProVideoPlayer` を 2 系統（Active/Standby）で運用し、停止やドリフト発生時に Resync して視聴継続性を高めることを目的にしています。

## 主な特徴

- 2 系統プレイヤー（Active/Standby）での切替運用
- `GetTime()` 前進確認ベースの Resync 判定
- `ResyncCoordinator` による予約制御と同時実行数制限
- スタッフ向け操作パネル（URL 更新、全体 Resync、停止、接続制限調整）
- 視聴者向け状態表示とローカル設定 UI（音量、無音時自動 Resync など）

詳細設計は [Docs/Design.md](Docs/Design.md) を参照してください。

## 動作環境

`Packages/tokyo.chigiri.pasocommate.auncast/package.json` に基づく情報です。

- Unity: `2022.3`
- Package name: `tokyo.chigiri.pasocommate.auncast`
- Display name: `AunCast`
- 依存 VPM パッケージ:
  - `com.vrchat.worlds >=3.10.2`
  - `net.narazaka.vrchat.tmp-fallback-fonts-jp >=1.0.0`
  - `tokyo.chigiri.pasocommate.rendermate >=1.0.0 <2.0.0`

## リポジトリ構成

- `Packages/tokyo.chigiri.pasocommate.auncast/`
  - AunCast 本体パッケージ
  - `AunCast.prefab`、`Prefabs/WallControlPanel.prefab`
  - Udon スクリプト群（`Scripts/Udon/*`）
- `Assets/AunCast-Dev/AunCast-Verify.unity`
  - 検証用シーン
- `Docs/`
  - 設計、実装パターン、QA、ローカル RTSP 検証手順

## 主要コンポーネント

- `LocalDualPlayerController`
  - Active/Standby の 2 系統再生と切替制御
- `ResyncCoordinator` / `ResyncCoordinatorClient`
  - ワールド全体の Resync 要求キューと実行調停
- `PlaybackSwitcher` / `PlaybackMonitor` / `ActivePlayerMonitor`
  - 切替進行、状態監視、異常検知
- `AudioSilenceDetector`
  - 無音区間検知
- `StaffControlPanel` / `UserStatusPanel` / `WallControlPanel`
  - スタッフ操作 UI・視聴者 UI・壁面操作 UI

## 導入・検証

### このリポジトリで開発する場合

1. リポジトリを clone する
2. Unity Hub でプロジェクトを開く
3. `Assets/AunCast-Dev/AunCast-Verify.unity` で動作確認する

### ローカル RTSP で検証する場合

- [Docs/Local-Test-Server.md](Docs/Local-Test-Server.md) を参照してください
- MediaMTX + FFmpeg で `rtsp://` / `rtspt://` ストリームを作って検証できます

## 開発ルール（要点）

- 作業前に `CLAUDE.md` を確認
- 同期変数・スタッフ権限操作・UI 追従は [Docs/Implementation-Patterns.md](Docs/Implementation-Patterns.md) のパターンに合わせる
- `[UdonSynced]` を追加/変更した場合は `Tools > UdonSharp > Refresh All UdonSharp Programs` を実行して `.asset` を更新

## QA

- 自動/手動の検証観点は [Docs/QA-Checklist.md](Docs/QA-Checklist.md)
- テストコード（EditMode）: `Assets/AunCast-Dev/Tests/Editor/`

## リリースとワークフロー

- リリース作成: `.github/workflows/release.yml`（手動実行）
- リポジトリ listing 再構築: `.github/workflows/build-listing.yml`

`release.yml` では、以下を設定すると別リポジトリの listing ワークフローを自動起動できます。

- Variables
  - `VPM_LISTING_REPOSITORY`（`owner/repo` 形式）
  - `VPM_LISTING_WORKFLOW`（省略時 `build-listing.yml`）
  - `VPM_LISTING_REF`（省略時 `main`）
- Secret
  - `VPM_LISTING_REPO_TOKEN`（対象リポジトリの Actions 実行権限を持つトークン）

## ライセンス

- 本体ライセンス: `Packages/tokyo.chigiri.pasocommate.auncast/LICENSE`
- サードパーティ表記: `Packages/tokyo.chigiri.pasocommate.auncast/THIRD_PARTY_NOTICES.md`
- VN3 ライセンス文書: `Packages/tokyo.chigiri.pasocommate.auncast/vn3license_ja.pdf`, `vn3license_en.pdf`
