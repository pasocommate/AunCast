---
title: AunCast ユーザーマニュアル
description: VRChatワールド向けの低遅延ライブ専用ビデオプレイヤー AunCast のユーザーマニュアル。ライブ演奏やDJプレイの音声を、長時間ズレを蓄積させず途切れさせずにワールド内へ配信し続けます。
image: https://pasocommate.chigiri.tokyo/auncast/assets/auncast-og-card.jpg
---

# AunCast ユーザーマニュアル

![AunCast ユーザーマニュアル](./assets/auncast-banner.jpg){ width="836" }

**AunCast** は、VRChatワールド向けの **低遅延ライブ専用ビデオプレイヤー** です。インスタンス内で出演者が行うアバターパフォーマンスに合わせて、**ライブ演奏・DJプレイなど** の音声をワールド内に配信するイベントで、長時間にわたってズレを蓄積させず、途切れずに配信し続けることを目的としています。

---

## 動作環境

- **Unity 2022.3**（VRChat ワールド開発の推奨バージョン）
- **VRChat SDK - Worlds**（VCC でワールドプロジェクトを作成すると導入されます）
- その他の依存パッケージは、VCC がインストール時に自動的に解決します。
- **対応プラットフォーム**：PC（Windows）および Quest などの Android 単体機。ただし Quest で配信の音声・映像を再生するには、**配信URLが Quest で再生可能な形式（HLSなど）である必要があります**。TopazChat などの `rtspt://` 形式は Quest では再生できません（→ [配信・運用上の注意](operation/streaming.md#quest-url)）。

---

## このマニュアルの読み方

まず、**[動作原理](how-it-works.md)** と **[クイックスタート](quickstart.md)** に目を通せば、基本的な利用に必要な事項を把握できます。詳細な仕様は、その後の各章を必要に応じて参照してください。
