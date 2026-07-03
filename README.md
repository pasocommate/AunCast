![AunCast Banner](Packages/tokyo.chigiri.pasocommate.auncast/Textures/Editor/auncast-banner.jpg)

# AunCast

**AunCast** は、VRChat ワールド向けの **低遅延ライブ専用ビデオプレイヤー** です。  
音楽ライブやDJイベントなど、アバターパフォーマンスに合わせて音声を配信するシーンで、**長時間にわたってズレず・途切れずに配信し続ける** ことを目指して開発されました。

## 特徴

- **観客向けパネル** — 再生状態の確認、音量調整、手動Resyncなどの機能を提供します。ジェスチャーでいつでも手元に呼び出せます。
  
  <img src="Manual/src/assets/portable-panel-viewer.png" alt="観客向けパネル" width="49%">
- **スタッフ操作パネル** — URL更新、接続上限調整などをワールド内で操作できます。観客向けパネルとの切り替え表示が可能です。
  
  <img src="Manual/src/assets/portable-panel-staff.png" alt="スタッフ操作パネル" width="49%">
- **２系統プレイヤーによるシームレスな再同期** — 現用＋予備の２系統を保持し、予備をバックグラウンドで接続してから切り替えます。ドリフト（ズレ）を解消するためのResync（再同期）を行う際に、音声の途切れを最小限に抑えます。
  
  ![Resyncの流れ（概念図）](Manual/src/assets/diagram-resync-switch.svg)
- **予約制の再同期キュー** — 同時に再同期できる人数を制御し、配信サーバーの同時接続上限を超過させません。
  
  ![再同期の順番待ち（概念図）](Manual/src/assets/diagram-resync-queue.svg)

## 導入

VCC（VRChat Creator Companion）からインストールできます。

1. https://pasocommate.chigiri.tokyo/ を開いて **Add to VCC** をクリックし、VCCに PasocomMate リスティングを登録します。
2. 対象プロジェクトの **Manage Project** で **AunCast** を追加します。

詳しい手順は **[クイックスタート](https://pasocommate.github.io/AunCast/quickstart/)** をご覧ください。

## ドキュメント

| ドキュメント | 対象 | 内容 |
|---|---|---|
| **[ユーザーマニュアル](https://pasocommate.github.io/AunCast/)** | ワールド制作者・イベントスタッフ | 導入手順、設定、運用ガイド、トラブルシューティング |
| **[設計ドキュメント](Docs/Design.md)** | 開発者・AIエージェント | システム設計、状態遷移、同期モデル |
| **[実装パターン](Docs/Implementation-Patterns.md)** | 開発者・AIエージェント | 同期変数、スタッフ権限操作、UI双方向追従のコーディングルール |

## 動作環境

- **Unity 2022.3**
- **VRChat SDK - Worlds** 3.10.2 以上
- **対応プラットフォーム**: PC（Windows）のみ

その他の依存パッケージはVCCが自動的に解決します。

## ライセンス

- [利用規約](Packages/tokyo.chigiri.pasocommate.auncast/LICENSE)
- [サードパーティ表記](Packages/tokyo.chigiri.pasocommate.auncast/THIRD_PARTY_NOTICES.md)
- VN3 ライセンス: [日本語](Packages/tokyo.chigiri.pasocommate.auncast/vn3license_ja.pdf) / [English](Packages/tokyo.chigiri.pasocommate.auncast/vn3license_en.pdf)
