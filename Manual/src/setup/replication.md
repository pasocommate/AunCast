# 複製配置と再配線（スタッフ向け）

スクリーンの追加や音声出力の配線など、**シーン上のオブジェクトを複製・配置してから、`AunCastSettings` のボタンで再配線する**作業をまとめています。インスペクタの値だけで設定できる項目は[設定と調整](settings.md)を参照してください。

<!--@rv 作業する前にシーンのバックアップを取るよう促す注意書き -->

---

## スクリーンを増やす {#screens}

映像の出力先（スクリーン）は複製できます。**同一のマテリアルを共有していれば、スクリーンを増やしてもビデオデコードの負荷はほぼ一定**です。

1. 既存の **`Screen` GameObject** を選択し、Ctrl+D で複製します。
2. ワールド内の任意の位置に配置し、サイズを調整します。
3. `AunCastSettings` の **出力・参照の再配線** にある **「AunCast参照を再配線」** を押し、コンポーネントを関連付けます。

別のシェーダー / マテリアルを使用する場合は、複製した `AunCastScreen` の以下の項目を合わせてください。

- **テクスチャプロパティ名**（`textureProperty`、既定 `_EmissionMap`）… そのシェーダーが映像に使用するテクスチャプロパティ名
- **レンダラーインデックス**（`rendererIndex`）… マルチマテリアルのどのスロットに適用するか

!!! note "停止中の画像（アイドル画像）"
    再生停止中の表示は、[設定と調整](settings.md)の **映像プレイヤー** セクションの **「停止中のスクリーン画像」** で指定します。再配線の際に各スクリーンへ分配設定されます。

## 壁パネルを増やす {#wall-panels}

壁パネル（`WallControlPanel`）は複製して、複数箇所に設置できます。会場の規模が大きい場合や、スタッフ控え室にも操作パネルを置きたい場合に有効です。

1. 既存の **`WallControlPanel` GameObject** を選択し、Ctrl+D で複製します。
2. ワールド内の任意の位置に配置します。
3. `AunCastSettings` の **出力・参照の再配線** にある **「AunCast参照を再配線」** を押し、コンポーネントを関連付けます。

## 音声（AunCastSpeaker 配線） {#speaker}

AunCast は内部に `PlayerA` / `PlayerB`（A/B再生系統）を持つため、音声出力にもそれぞれ専用の `AudioSource` が必要です。使用する `AudioSource` に `AunCastSpeaker` を付け、接続先を `PlayerA` または `PlayerB` に指定してから再配線します。

### 手順

1. 音声出力に使う `AudioSource` を用意し、位置・空間音響（3D設定）を調整します。既存ワールドの `VRCAVProVideoSpeaker` 付き `AudioSource` も使用できます。
2. `AudioSource` に `AunCastSpeaker` を追加し、`playerIndex`（0 = `PlayerA`、1 = `PlayerB`）と `baseVolume` を設定します。
3. 既存の AVPro スクリーン / スピーカーから移行する場合は、`AunCastSettings` の **既存プレイヤー出力の変換** セクションで候補を確認し、各行の **「変換」** を押します。
4. `AunCastSettings` の **出力・参照の再配線** にある **「AunCast参照を再配線」** を押します。同一シーン上の `AunCastScreen` / `AunCastUiScreen` / `AunCastSpeaker` / `AunCastAudioOutputTunnel` が再スキャンされ、音声・映像の配線が再構築されます。

    ![既存プレイヤー出力の変換セクション](../assets/inspector-avpro-speaker.png){ width="360" }

### 音量と既存 AudioSource の扱い

- `AudioSource.volume` は実行時に AunCast が上書きします。設計上の基準音量は `AunCastSpeaker.baseVolume` に設定してください。
- 観客がパネルで変更する音量は、`baseVolume` に乗算されます。複数の音声出力を置く場合は、各 `AunCastSpeaker` の `baseVolume` でバランスを取ります。
- **複数スピーカーの多点配置**も可能です。必要な数だけ `AunCastSpeaker` 付き `AudioSource` を置き、A/B再生系統ごとに `playerIndex` を設定します。
- 設定をやり直す場合は、不要な `AunCastSpeaker` を削除または無効化してから、再度 **「AunCast参照を再配線」** を押します。
- A/Bで同一 `AudioSource` を共有しているなどの配線不整合は、インスペクタ上に赤色で表示されます。

### AudioOutputTunnel 構成を移行する場合

TopazChat Player の「+ Reverb Filter」など、`AudioOutputTunnel` を使っている構成では、不可聴のダミーシンクを通常スピーカーとして変換しないでください。必要な場合は `AunCastAudioOutputTunnel` を追加し、既存の `leftOutput` / `rightOutput` / `stereoOutput` に相当する AudioSource を設定します。

`AunCastSettings` の再配線を実行すると、`AunCastAudioOutputTunnel.inputA` / `inputB` は同一シーンの `AunCastSpeaker` から自動設定されます。この方式は直結出力よりリングバッファ分の遅延が増えるため、通常の `VRCAVProVideoSpeaker` 直結構成では使わないでください。
