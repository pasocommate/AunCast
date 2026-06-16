# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2026-05-29
- WallControlPanel を検証シーンに追加
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
- PlaybackMonitor の残留ビットを所有者自身で掃除するよう修正

## [1.3.0] - 2026-05-13
- Stop All 時に Playing URL を空へリセットするよう修正
- AudioLink 未設定時の自動探索を追加
- AudioLink の自動配線を Inspector に追加
- WallControlPanel をプレハブ化し複数配置に対応
