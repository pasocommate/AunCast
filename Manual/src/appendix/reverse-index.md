---
description: 「○○したい」「○○が起きている」から該当ページへ案内する逆引きインデックス。観客・スタッフそれぞれの目的別に整理しています。
image: https://pasocommate.chigiri.tokyo/auncast/assets/auncast-og-card.jpg
---

# 逆引きインデックス

「○○したい」「○○が起きている」から、該当するページへ案内します。

---

## 観客として

| やりたいこと・起きていること | 参照先 |
|---|---|
| 手元パネルを呼び出したい | [壁パネルと手元パネル](../operation/panels.md) |
| 音声が出ない・ズレを直したい | [Resync ボタン](../operation/panel-viewer.md#resync-button)、[Volume](../operation/panel-viewer.md#volume) |
| 映像が止まった・カクつく | [Resync ボタン](../operation/panel-viewer.md#resync-button)、[Reboot ボタン](../operation/panel-viewer.md#reboot-button) |
| 自動的に再同期されるのを止めたい | [Silence Resync](../operation/panel-viewer.md#auto-silence) |
| 音量を変えたい | [Volume](../operation/panel-viewer.md#volume) |
| 表示の意味を知りたい（Drift, Audio Level 等） | [手元パネルの各部](../operation/panel-viewer.md#controls) |
| 「Retry Wait」「Error」が表示されている | [再生ステータス表示](../operation/panel-viewer.md#playback-status) |
| Resync ボタンが押せない | [Resync ボタン](../operation/panel-viewer.md#resync-button) |
| AunCast のバージョンを確認したい | 壁パネルの **情報ボタン**（<span class="material-symbol" aria-hidden="true">&#xE88E;</span>）で表示されます |

---

## スタッフとして（配信中）

| やりたいこと・起きていること | 参照先 |
|---|---|
| スタッフビューを解錠したい | [スタッフビューを解錠する](../operation/panel-staff.md#unlock) |
| 配信URLを変更したい | [配信URLを変更する](../operation/panel-staff.md#change-url) |
| 全員をまとめて再同期したい | [Resync All](../operation/panel-staff.md#global-resync) |
| 全員の再生を止めたい | [Stop All](../operation/panel-staff.md#stop-all) |
| 同時接続上限・同時Resync上限を調整したい | [上限の調整](../operation/monitoring.md#limits) |
| エラー（赤）が増えている | [異常な状況への対処](../operation/monitoring.md#troubleshooting) |
| Resync Allの完了が遅い | [増減の目安](../operation/monitoring.md#scaling-guide) |
| 全員の再生がまとめて止まった | [Reboot All](../operation/panel-staff.md#force-reboot) |

---

## スタッフとして（初期設定）

| やりたいこと・起きていること | 参照先 |
|---|---|
| 導入の最短手順を知りたい | [クイックスタート](../quickstart.md#setup) |
| 接続上限を設定したい | [接続上限の設定](../setup/settings.md#connection-limits) |
| 停止中の待機画像を設定したい | [設定と調整](../setup/settings.md) |
| 壁パネルを追加したい | [壁パネルを増やす (AunCastWallControlPanel)](../setup/parts-placement.md#wall-panels) |
| スクリーンを追加したい | [スクリーンを増やす (AunCastScreen)](../setup/parts-placement.md#screens) |
| 音声出力を配線したい | [スピーカーを増やす (AunCastSpeaker)](../setup/parts-placement.md#speaker) |
| 既存ワールドのビデオプレイヤーから移行したい | [既存ワールドからの移行](../setup/parts-placement.md#migration) |
| TopazChat「+ Reverb Filter」構成を移行したい | [AudioOutputTunnel 構成を移行する場合](../setup/parts-placement.md#tunnel) |
| AudioLink と連携したい・`AudioLinkInput` が無効化された | [AudioLink をお使いの場合](../setup/parts-placement.md#audiolink) |
| 何名まで耐えられるか知りたい | [同時接続上限の管理](../concepts/connection-limit.md) |
| 配信サーバーの選び方を知りたい | [配信サーバーの選定](../operation/streaming.md#server-selection) |
| 配信のビットレートや遅延の目安を知りたい | [配信・運用上の注意](../operation/streaming.md) |

---

## よくある質問（FAQ）

??? question "再同期すると、全員の再生位置がそろうのか？"
    いいえ。再同期は **各観客が個別に自分の配信を復旧する** ものです。全員の再生位置を一致させる機能ではありません。Resync Allも「各自が順番に復旧する」だけです。

??? question "なぜ再同期は順番待ちになるのか？"
    全員が一斉に接続し直すと、配信サーバーの上限を超過し、かえって不安定になるためです（[同時接続上限の管理](../concepts/connection-limit.md)）。

??? question "リブート（Reboot）と再同期（Resync）の違いは？"
    再同期は予備系統を背後で準備してから切り替えるため **ほぼ途切れません**。リブートはいったんすべて停止して接続し直すため **途切れます**。

??? question "音量を変えると、他の観客にも反映されるのか？"
    いいえ。Volume はローカル（自分の端末）のみの設定で、他の観客や配信そのものには影響しません。スタッフ用の機能ではなく、観客自身が利用するための機能です。

??? question "ライブ配信ではなく録画動画でも使用できるか？"
    使用できますが、AunCast はライブ配信向けに設計されています。巻き戻しが可能な録画動画では、一般的な動画再生システムのほうが適する場面もあります。

??? question "Quest（Android 単体機）のユーザーにも視聴できるか？"
    いいえ。AunCast は **PC（Windows）のみ対応** で、Quest などの Android 単体機には対応していません。Quest 環境では配信の音声・映像が正常に再生されません。PC/Quest 混在のイベントでは、そのことを前提に運用してください。

---

## 解決しない場合は

上記で解決しない場合は、GitHub の Issue でご連絡ください。過去に同じ報告がないか確認したうえで、目的に合ったテンプレートを選んで投稿できます。

- **[不具合報告](https://github.com/pasocommate/AunCast/issues/new?template=bug-report.yml)**：想定外の動作や再現可能な不具合
- **[質問・相談](https://github.com/pasocommate/AunCast/issues/new?template=question.yml)**：マニュアルを読んでも解決しなかった疑問
- **[機能要望](https://github.com/pasocommate/AunCast/issues/new?template=feature-request.yml)**：新しい機能や改善の提案

[Issue 一覧はこちら](https://github.com/pasocommate/AunCast/issues)で確認できます。
