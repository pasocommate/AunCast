# 逆引きインデックス

「○○したい」「○○が起きている」から、該当するページへ案内します。

---

## 観客として

| やりたいこと・起きていること | 参照先 |
|---|---|
| 手元パネルを呼び出したい | [壁パネル](../operation/wall-panel.md) |
| 音声が出ない・ズレを直したい | [Resync ボタン](../operation/panel-viewer.md#resync-button)、[Volume](../operation/panel-viewer.md#volume音量) |
| 映像が止まった・カクつく | [Resync ボタン](../operation/panel-viewer.md#resync-button)、[Reboot ボタン](../operation/panel-viewer.md#reboot-button) |
| 自動で再同期されるのを止めたい | [Silence Resync](../operation/panel-viewer.md#auto-silence) |
| 音量を変えたい | [Volume](../operation/panel-viewer.md#volume音量) |
| 表示の意味を知りたい（Drift, Audio Level, ステータス等） | [手元パネルの各部](../operation/panel-viewer.md#手元パネルの各部) |
| 「Retry Wait」「Error」が表示されている | [再生ステータス表示](../operation/panel-viewer.md#再生ステータス表示サムネイルの下) |
| Resync ボタンが押せない | [Resync ボタン](../operation/panel-viewer.md#resync-button) |
| AunCast のバージョンを確認したい | 壁パネルの (i) ボタンで表示される製品情報に記載されています |

---

## スタッフとして（配信中）

| やりたいこと・起きていること | 参照先 |
|---|---|
| スタッフ画面を解錠したい | [スタッフ画面を解錠する](../operation/panel-staff.md#unlock) |
| 配信URLを変更したい | [配信URLを変更する](../operation/panel-staff.md#配信urlを変更する) |
| 全員をまとめて再同期したい | [Resync All](../operation/panel-staff.md#global-resync) |
| 全員の再生を止めたい | [Stop All](../operation/panel-staff.md#stop-all全員の再生を停止) |
| 同時接続上限・同時Resync上限を調整したい | [上限の調整](../operation/monitoring.md#limits) |
| エラー（赤）が増えている | [異常な状況への対処](../operation/monitoring.md#異常な状況への対処) |
| Resync Allの完了が遅い | [増減の目安](../operation/monitoring.md#増減の目安) |
| 全員の再生がまとめて止まった | [Reboot All](../operation/panel-staff.md#force-reboot) |

---

## スタッフとして（初期設定）

| やりたいこと・起きていること | 参照先 |
|---|---|
| 導入の最短手順を知りたい | [クイックスタート](../quickstart.md#setup) |
| 接続上限を設定したい | [接続上限の設定](../setup/settings.md#connection-limits) |
| 停止中の待機画像を設定したい | [設定と調整](../setup/settings.md) |
| スクリーンを追加したい | [スクリーンを増やす](../setup/replication.md#screens) |
| 壁パネルを追加したい | [壁パネルを増やす](../setup/replication.md#wall-panels) |
| 音声出力を配線したい | [音声（AVPro Speaker 配線）](../setup/replication.md#speaker) |
| 何名まで耐えられるか知りたい | [同時接続上限の管理](../concepts/connection-limit.md) |
| 配信サーバーの選び方を知りたい | [配信サーバーの選定](../operation/streaming.md#配信サーバーの選定) |
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
