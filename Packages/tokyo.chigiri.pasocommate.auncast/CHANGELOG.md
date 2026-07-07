# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.2.2] - 2026-07-08
- 停止中スクリーン画像の縦横比が崩れる問題を修正

## [4.2.0] - 2026-07-07
- 音声のみの再生フォールバックを追加（映像が取得できない場合などに音声のみで再生を継続）
- 映像テクスチャの共有方式を整理し、ガンマ補正・上下反転の扱いを改善

## [4.1.0] - 2026-07-06
- AudioOutputTunnel への移行導線を追加（出力移行 UI を行ごとの変換方式へ刷新し、再配線対象にトンネルを追加）
- 利用規約同意の保存先を ProjectSettings ストアへ移行
- トンネル移行と壁パネル再配線の不具合を修正
- EditorOnly 出力を再配線対象から除外するよう修正
- インスペクタの HelpBox で日本語の折り返しを改善

## [4.0.0] - 2026-07-04
- 出力コンポーネントを `AunCast` プレフィックスに統一し、宣言モデルへ刷新（**破壊的変更**: コンポーネント名・配線手順が変わります）
- 既存ワールドの移行支援を追加（コンポーネントの再配線・不要スピーカーの無効化などを含む）
- シーンへ AunCast を配置するメニューを追加

## [3.1.0] - 2026-07-03
- テーマ適用範囲を拡充（パネル余白・壁面サイズ・ビデオ画面・StaffLock・表示エリア背景色などをテーマから制御）
- 新テーマ Flat を追加
- ThemeApplier にライブ自動適用トグルを追加
- インスペクタ UI を日英両対応化（表示言語の手動トグル付き）
- 利用規約への同意ゲートをインスペクタに追加
- AunCastSettings 編集時に UI 表示を実値へ同期
- リトライ間隔を AunCastSettings で調整可能に
- AunCastWallControlPanel に製品情報ビュー（QR コード・連絡先・バージョン付きコピーライト表記）を追加
- 「Auto Resync」表記を「Silence Resync」に統一
- DriftGraph（同期デバッグ表示）を廃止
- ワールド定員管理を廃止し接続上限を既定値化
- RenderMate v3 に対応
- ユーザーマニュアル（MkDocs）を新設・整備
- 同意状態が既定値へ戻る不具合、ビデオプレビューの FlipY/Gamma、Play 停止後の背景色整合など各種バグ修正

## [3.0.0] - 2026-06-16
- PlayerData によるローカル設定の永続化
- デフォルト配信 URL と初回 Join 時の自動再生
- スクリーン: idle テクスチャの上下反転・ガンマ二重補正・黒帯を修正
- スクリーン: idle テクスチャを Blank-AunCast.png に切り替え
- UI: グラブ中に右スティック召喚ジェスチャーを抑制
- 各種バグ修正

## [2.1.0] - 2026-05-29
- AunCastWallControlPanel を検証シーンに追加
- ドリフト計測中断時に基準点をクリアし再有効化時に取り直すよう修正

## [2.0.0] - 2026-05-27
- テーマシステムを ScriptableObject + Applier 構成に刷新し、Inspector インライン編集に対応
- allowedUserNames による名前指定スタッフ解錠を追加
- ダブルトリガージェスチャーを左右手で独立設定可能に
- HUD 配置を視界中央に変更し、VR/Desktop 個別オフセット調整に対応
- URL 送信者名のホバー表示・タイムラインログトグル・スタッフロックを追加
- スタッフ操作 UI のボタン状態管理を改善
- クロスフェード時の白フラッシュ防止・ドリフト Resync 安定化など各種バグ修正

## [1.5.0] - 2026-05-13
- AudioLink ランタイム AutoAssign の型名フィルタ欠落と Reboot 時の切替漏れを修正

## [1.4.0] - 2026-05-13
- AVPro Speaker 自動配線ツール・defaultVolume 外出し・AudioSource 初期音量保持を追加
- ビルド時に VRC_SceneDescriptor の Capacity を自動注入する機能を追加
- AunCastPlaybackMonitor の残留ビットを所有者自身で掃除するよう修正

## [1.3.0] - 2026-05-13
- Stop All 時に Playing URL を空へリセットするよう修正
- AudioLink 未設定時の自動探索を追加
- AudioLink の自動配線を Inspector に追加
- AunCastWallControlPanel をプレハブ化し複数配置に対応
