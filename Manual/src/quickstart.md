---
description: AunCast の導入から配信開始までの最短手順。スタッフの初期設定と、観客・スタッフそれぞれの基本操作をまとめています。
image: https://pasocommate.chigiri.tokyo/auncast/assets/auncast-og-card.jpg
---

# クイックスタート

本システム利用時の最短手順です。詳細な仕様は各リンク先を参照してください。

---

## スタッフ：導入・初期設定 {#setup}

1. **[VCCにリスティングを追加](vcc://vpm/addRepo?url=https://pasocommate.chigiri.tokyo/index.json)** をクリックしてリンクを開き、VCCに PasocomMate リスティングを登録します。うまく開けない場合は、VCCの **Settings → Packages → Add Repository** より以下のリスティングURLを登録します。

    ```
    https://pasocommate.chigiri.tokyo/index.json
    ```

2. 対象プロジェクトの **Manage Project** で、**AunCast** をプロジェクトに追加します。
3. メニューより **`Tools → PasocomMate → AunCast → Create AunCast`** を選択し、シーンに `AunCast` を配置します。`AunCast` オブジェクトの直下には、いくつかのカスタマイズ可能な部品が含まれています。１つのシーンに配置できる `AunCast` は１つだけです（すでに配置済みの場合は追加されず、その旨が表示されます）。

    ![AunCastヒエラルキー](assets/hierarchy-auncast.png){ width="200" }

4. スクリーン（`ExampleOutput/Screen`）、スピーカー（`ExampleOutput/SpeakerA`, `ExampleOutput/SpeakerB`）、壁パネル（`WallControlPanel`）などの位置を調整します。壁パネルを複数箇所から操作したい場合は、`WallControlPanel` を複製して配置します。手元パネル（`PortablePanel`）は編集の邪魔にならない場所に置いてください。
5. `AunCast` ルートオブジェクトを選択し、**`AunCastSettings`** コンポーネントのインスペクタで**利用規約に同意**します。日本語フォントの警告が表示されている場合は、メニューより `Tools → TextMesh Pro VRC Fallback Font JPを設定` を実行します。
6. **`AunCastSettings`** コンポーネントのインスペクタで、スタッフ用の **壁パネル解錠パスコード** と、必要に応じて **スタッフ許可ユーザー名**（VRChat ディスプレイネーム）を設定します。
7. **`AunCastSettings`** で、**同時接続上限**（Connection≤）を利用する配信サーバの同時接続上限に設定します。多くの場合は 100 のままで大丈夫です。**同時Resync上限**（Concurrent≤）も 10 のまま開始してください。いずれもワールド内でリアルタイムに変更できます。 [→ 同時接続上限の管理](concepts/connection-limit.md#limit-params)
8. オプション：既存ワールドのビデオプレイヤーから移行する場合は、**既存プレイヤー出力の変換** で **「候補を再検出」** を押し、検出された各行で接続先（`PlayerA` / `PlayerB` / 自動複製）を選んで **「変換」** を押します。続けて **出力・参照の再配線 →「参照関係を再配線」** を押します。 [→ 既存ワールドからの移行](setup/parts-placement.md#migration)
9. **動作確認**：テスト配信で動作を確認します。配信の再生には実際の VRChat クライアントが必要で、**ClientSim では配信の再生を確認できません**。ワールドをアップロード（または Build & Test）してインスタンスに入り、スタッフビューからテスト配信のURLを入力してください。

[→ 設定と調整（詳細）](setup/settings.md)

---

## スタッフ：配信管理 {#staff}

1. 壁パネルの鍵アイコン（<span class="material-symbol" aria-hidden="true">&#xE899;</span>）を押し、**暗証番号** でスタッフビューを解錠します。**スタッフ許可ユーザー名** に登録されている場合、この手順は不要です。

    ![壁パネル（暗証番号の入力）](assets/wall-panel-staff-passcode.png){ width="260" }

2. 手元パネルを表示します。壁パネルの **Spawn Panel** ボタン（<span class="material-symbol" aria-hidden="true">&#xE5D2;</span>）、または **Spawn Gesture** で選択したジェスチャー・キーバインドで呼び出せます。初期状態では、VRは右スティック上方向長押し、デスクトップはTabキー２回押しです。

    <div class="figure-row">
      <img src="../assets/wall-panel-user-desktop.png" alt="壁パネル（近づいたとき）" width="270">
      <img src="../assets/portable-panel-viewer.png" alt="手元パネル（観客ビュー）" width="324">
    </div>

3. 手元パネルをスタッフビューへ切り替えます。ジェスチャーをもう一度行うか、パネル右上の **ビュー切替ボタン**（<span class="material-symbol" aria-hidden="true">&#xE8D4;</span>）を押します。

    ![スタッフ操作画面](assets/portable-panel-staff.png){ width="360" }

4. **Next URL** に配信URLを入力し、右側の **送信ボタン**（<span class="material-symbol" aria-hidden="true">&#xE8D5;</span>）で配信を開始します。
5. 配信を終えたら **Stop All** で停止します。

[→ 壁パネルと手元パネル](operation/panels.md) / [手元パネル：スタッフビュー](operation/panel-staff.md)

---

## 観客：視聴とトラブル時の操作 {#viewer}

1. **手元パネルを表示する**：前述の手順どおり、手元パネルを表示します。

    ![手元パネル（観客ビュー）](assets/portable-panel-viewer.png){ width="360" }

2. **音声がズレた**ときは **Resync ボタン**を押します。しばらく待てば順番が回り、音声が途切れることなく復旧します。通常は自動検知で行われるため、この操作は不要です。

3. 再生が安定せず復旧しない・Resync の順番が回ってこない場合は、**Reboot ボタン**（<span class="material-symbol" aria-hidden="true">&#xEA0B;</span>）を使用します。音声・映像が一時的に途切れます（[→ Reboot ボタン](operation/panel-viewer.md#reboot-button)）。

[→ 手元パネル：観客ビュー](operation/panel-viewer.md)
