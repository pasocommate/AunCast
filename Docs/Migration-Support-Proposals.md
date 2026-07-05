# AunCast 既存ワールド移行支援 — 機能提案書

既存ワールドで稼働中のライブプレイヤー（TopazChat Player / iwaSync3 / VizVid / USharpVideo 等）を AunCast に置き換える際の障壁を実ワールドの移行検証（2026-07）で洗い出し、導入・移行を容易にするための機能追加・構造変更を提案する。各項目のステータスは【対応決定】または【提案】。

## 背景となる調査知見

### 既存プレイヤーの標準構成

- **スクリーン**: `VRCAVProVideoScreen` + 任意の Mesh/Material。ビデオテクスチャの適用先プロパティは `textureProperty`（`_MainTex` 等）で指定される。
- **スピーカー**: 標準は `VRCAVProVideoSpeaker` 付き AudioSource の直結構成。プレイヤー本体（Udon）が自分のスピーカー AudioSource を音量制御・AudioLink 割り当てのために参照するパターンが共通して存在する（この参照はプレイヤーごと撤去されるため移行時は無害）。
- **URL**: プレイヤーの UdonBehaviour が VRCUrl 型の public 変数を持つ（例: オリジナル TopazChatPlayer は UdonGraph 製で、public 変数は単一の `StreamURL` と `VideoPlayer` のみ）。ワールド制作者がプレイヤーを独自スクリプトで差し替え・拡張しているケースがあるため、**変数名ベースではなく VRCUrl 型ベースの列挙**でないと一般化できない。

### PCM トンネル構造（AudioOutputTunnel）

TopazChat Player の「+ Reverb Filter」バリアントのみ、以下の特殊構造を持つ:

```
VRCAVProVideoPlayer
 └─ VRCAVProVideoSpeaker(Stereo) on 不可聴ダミーシンク
     │  （カスタムロールオフ全キー 0 で聞こえない AudioSource）
     └─ AudioOutputTunnel（GetOutputData で PCM 吸出 → AudioClip リングバッファ書込）
         └─ 素の AudioSource 群（input / leftOutput / rightOutput / stereoOutput）
            ここに Unity DSP（AudioReverbFilter 等）や外部音量制御が適用される
```

AVPro 音声は Unity の DSP フィルタチェーンに乗らないため、通常の AudioClip 再生に変換してフィルタを効かせるための仕組み。**実際に聞こえるスピーカーに VRCAVProVideoSpeaker が付いていない**ため、素朴なスピーカー検出では正しい候補が得られない。

### 主要プレイヤーのトンネル構造調査（2026-07-04）

判定指標: `GetOutputData` / `OnAudioFilterRead` / `AudioClip.SetData` の使用と「AudioSource から PCM を読むコンポーネント」の有無。

| プレイヤー | 結果 |
|---|---|
| TopazChat Player | 「+ Reverb Filter」版のみ **AudioOutputTunnel あり** |
| iwaSync3 | なし（`VideoCore` の `AudioSource[] speaker` は音量書き込み専用） |
| VizVid (VVMW) | なし（`Core_Audio` は音量フェード、`Core_AudioLink` は AudioLink 割り当てのみ） |
| USharpVideo | なし（`VideoPlayerManager.audioSources` は音量書き込み専用） |

結論: **PCM トンネル構造は TopazChat「+ Reverb Filter」版に固有**。互換対応の同梱対象は AudioOutputTunnel のみで足りる。

## 目標とするセットアップ手順（UX 像・2026-07-04 ユーザー確定）

提案 1〜3 と 6 の実装後、ユーザーのセットアップ手順は次の形になることを目標とする。

1. **スクリーン**: 使用したいメッシュに AunCastScreen を手動付与し、マテリアルプロパティ名を指定する。または一括変換ツール（3）でシーンをスキャンし、一覧のトグルを ON にして自動変換する。
2. **スピーカー**: 使用したい AudioSource に AunCastSpeaker を手動付与し、ボリュームを設定する（**ボリューム設定プロパティは AudioSource ではなく AunCastSpeaker 側に持つ**）。または一括変換ツール（3）で自動変換する。
3. **再配線**: AunCastSettings インスペクターの再配線ボタンを押す。**現在の配線を一旦破棄し、シーン上の AunCastScreen / AunCastSpeaker をスキャンして配線し直す**。AunCast プレハブに元から含まれる AunCastScreen / AunCastSpeaker は、これを行う前に削除しておくことで、カスタマイズされたスクリーン・スピーカーのみを使用してセットアップできる。

## 提案一覧

### 1.【提案】再配線スコープのシーン全体化（優先度: 最高）

`RewireEventBusAndConsumers`（`Scripts/Editor/AunCastSettingsInspector.cs`）は現在 AunCast ルート配下のみを `GetComponentsInChildren` で探索し、EventBus の `videoTextureSubscribers` を全上書きする。このため **AunCast ルート外に置いたスクリーンコンポーネントは配線されず、手動で購読者に加えても Play/ビルド時の自動再配線（AunCastAutoRewire / AunCastBuildCallback）で消される**。

探索をシーン全体に拡大する。1 シーン 1 AunCast 制約があるため衝突しない。再配線対象は 2 の宣言型コンポーネント（AunCastScreen / AunCastSpeaker 等）とし、**「ユーザーがあらかじめ変換ユーティリティで AunCast 系コンポーネントに置き換えたものを、再配線がすべて配線する」**という手順に固定する。これにより再配線ボタンの処理が「シーン内の宣言済み出力を漏れなく配線する冪等処理」として明確になり、建物階層に組み込まれて移動できないスクリーンメッシュにも対応できる。同期スキーマ変更なし。Design.md に「AunCast 階層外の出力コンポーネントをサポート」と明記する。

### 2.【提案・推奨方針】宣言型出力コンポーネント AunCastScreen / AunCastSpeaker と変換ユーティリティ

AVPro 系コンポーネントと 1:1 対応する AunCast 系コンポーネントを用意し、名前で対応付けを表現する。ユーザーが触るのはこの宣言型コンポーネントだけにして、**VRCAVProVideoSpeaker の付いた AudioSource を AunCast が直接取り扱わずに済む**ようにする。

| AVPro 系 | AunCast 系（宣言型） | 実体 |
|---|---|---|
| VRCAVProVideoScreen | **AunCastScreen** | 既存 VideoMeshScreen のリネーム |
| （UI 向け） | **AunCastUiScreen** | 既存 VideoUiScreen のリネーム |
| VRCAVProVideoSpeaker | **AunCastSpeaker** | 既存 AudioSilenceDetector のリネーム + 機能追加 |

**AunCastSpeaker（AudioSilenceDetector の兼任化）**: 既存の RMS 無音検知機能に加えて、「この AudioSource を AunCast の音声出力にする」という宣言マーカーの役割と、所属系統（PlayerA / PlayerB）・チャンネルモード（Stereo / Left / Right — `VRCAVProVideoSpeaker.mode` 相当）の指定を持たせる。**ボリューム設定プロパティも AunCastSpeaker 側に持つ**: `AudioSource.volume` は AunCast がランタイムで管理する出力値（ユーザー音量 × fadeGain 等の積）とし、設計上の基準音量は宣言側で保持する。現行の「シーン上の初期 volume を BaseVolume として暗黙にキャッシュする」方式より意図が明確になり、ランタイムに上書きされる値をユーザーが編集してしまう混乱も防げる。

**クローン生成は行わない（2026-07-04 ユーザー決定）**: AunCastSpeaker 付き AudioSource **そのもの**がランタイムのスピーカーであり、再配線はこれをそのまま配線して利用する。
- プレハブに既に含まれる PlayerA / PlayerB の AudioSource は、AudioSilenceDetector が AunCastSpeaker に差し替われば**そのまま使える**（系統指定は差し替え時に確定済み）。
- 別の AudioSource を使いたい場合は、内蔵のものを削除し、使いたい AudioSource を変換ユーティリティで AunCastSpeaker 化（A/B 各系統分）して再配線するだけ。
- 位置・音量・3D 設定の調整は該当 AudioSource を直接編集するだけで済む。「クローンへの反映」「調整のたびの再セットアップ」という概念自体がなくなる。

**再配線は冪等な純粋配線処理（破棄 → 再構築）**: 現在の配線を一旦破棄したうえで、シーン全体（提案 1）から AunCastScreen / AunCastUiScreen / AunCastSpeaker を収集し、eventBus 購読・`VideoPlayerManager.audioSources`・SilenceDetector 参照・背後の VRCAVProVideoSpeaker（存在保証と videoPlayer 参照の修復）を配線し直す。**オブジェクトの生成・削除・複製は一切行わない**。何度実行しても同じ結果になり、Play/ビルド時の自動実行も安全になる。

**変換（明示的・一回きりの操作。オブジェクトの作成・改変はこちらに集約）**: 手動付与（コンポーネントを Add してプロパティを設定）と、3 の一括変換ツールの 2 通り。変換の内容は共通:
- VRCAVProVideoScreen → AunCastScreen: `textureProperty` をそのまま引き継ぐ。AVPro コンポーネントを除去。プロパティ名の手動調査が不要になる。
- VRCAVProVideoSpeaker 付き AudioSource → AunCastSpeaker 化: 所属系統を指定して変換（`mode` はそのまま引き継ぎ、元の `AudioSource.volume` は AunCastSpeaker のボリュームへ転写）。AudioSource の設定（3D・ロールオフ・フィルタ等）は無傷で維持。
- 接続先の選択肢は **PlayerA / PlayerB / 自動複製 (A/B)** の 3 つ。「自動複製」は、同じ位置に同じ設定の AudioSource を A/B 用に用意したいケース向けに、その AudioSource を同じ階層の直後に複製し、オリジナルを PlayerA・複製を PlayerB へ割り当てる（この変換時に明示的に一度だけ複製する）。

**プロパティ命名の原則**: AunCast 系コンポーネントのプロパティ名は、**なるべく VRCAVPro 系コンポーネントと共通の名称にする**。AunCastScreen のテクスチャプロパティ指定は現 VideoMeshScreen の `texParam` から `textureProperty` へ改名、AunCastSpeaker のチャンネルモードは `mode`、など。AVPro 系との対応関係が名前から自明になり、変換時の転写も同名コピーになる。

**留意点**: 公開クラスのリネームは `.cs` / `.asset` の GUID を維持して行い、既存ワールドの参照を壊さない（UdonSharp はクラス名とファイル名の一致が必要なため、ファイルリネームで対応）。AunCast.prefab 内の既定スクリーン・スピーカーも同モデルへ移行し、マニュアル・Design.md を更新する。既存の「AVPro Speaker 出力先セットアップ」（複製方式: `AunCastSpeakerRefs_A/B` コンテナ生成 + 元の EditorOnly 化）は本モデルで置き換えて廃止する。系統指定の持ち方（AunCastSpeaker のフィールドか、背後の VRCAVProVideoSpeaker.videoPlayer 参照を正とするか）は実装時に確定。

### 3.【提案】一括変換ツール（スクリーン・スピーカー同時変換）

スクリーンとスピーカーの自動変換を**単一のツール**で行う。シーンをスキャンして VRCAVProVideoScreen / VRCAVProVideoSpeaker（および他プレイヤー固有の類似コンポーネントへ拡張可能な検出）を**トグル付き一覧**で表示し、ON のものを AunCastScreen / AunCastSpeaker に一括変換する。

- **推測による自動設定**: スクリーン変換時はマテリアルプロパティ名を元コンポーネント（`VRCAVProVideoScreen.textureProperty` 等。取れない場合は対象マテリアルのシェーダーが持つ `_MainTex` / `_EmissionMap` の検出）から、スピーカー変換時はボリュームを元の `AudioSource.volume` 等から、可能な限り推測して自動設定する。
- 6 の要件（不可聴シンクも必ず列挙・状態ラベル・トンネル検知時の案内・外部参照ラベル）の表示基盤を兼ねる。
- 5 の移行アシスタントは、このツールの検出・変換に旧プレイヤーの撤去案内を加えたもの。

### 4.【提案】AudioLink 参照のエディタ時配線

現状の `GameObject.Find("AudioLink")` + 型名一致によるランタイム探索は GameObject 名の変更に弱い。再配線処理でシーンの AudioLink を検出し `PlaybackSwitcher.audioLinkBehaviour` をシリアライズ配線しておく（ランタイム探索はフォールバックとして残す）。

**【対応決定・2026-07-04】AudioLink 付属スピーカー（AudioLinkInput）の無効化＋EditorOnly 化**: `AudioLink.prefab` は `AudioLinkInput` 子オブジェクトに `AudioSource + VRCAVProVideoSpeaker` を同梱し、AudioLink の `audioSource` に配線している。AunCast はランタイムで `PlaybackSwitcher.SwitchAudioLinkSource()` が AudioLink の入力を Active スピーカーの `AudioSource` へ差し替えるため、この付属スピーカーは不要。従来はこれがセットアップ時に「スピーカー変換候補」として誤検出され、手動削除候補一覧にも並んでいた。対応として:

- 変換候補・手動削除候補の双方から AudioLink 付属スピーカーを除外する（`IsAudioLinkOwnedSource`: 対象コンポーネントの祖先に AudioLink 型 or 名 `AudioLink` があるかで判定）。
- 再配線時（`NeutralizeAudioLinkInputs`）に AudioLinkInput の GameObject を**無効化（SetActive false）＋EditorOnly タグ化**する。削除ではなく EditorOnly 化にするのは、削除だとシーン構造からの消失が混乱のもとになるため（エディタには残しつつビルド時に剥がす）。冪等。
- あわせて、無効化した AudioLinkInput の**すぐ隣（同じ親・直後の兄弟）に注記オブジェクトを生成**する（`EnsureAudioLinkInputNote`）。名前は英語で `AudioLink's referenced audio source is managed automatically by AunCast`、EditorOnly・非アクティブ。エディタでヒエラルキーを見た制作者が「なぜ AudioLinkInput が無効なのか」を理解できるようにするための説明用マーカー。既存の注記があれば再生成しない（冪等）。
- 提案 6/`DrawResidualCleanupGuidance` の「AunCast は自動削除・自動 EditorOnly 化を行わない」原則の**例外**。旧プレイヤー由来の残存物と異なり、AudioLink 入力は AunCast が完全に管理する対象であるため。

### 5.【提案】移行アシスタント（1〜3 の統合）

**AunCastSettings インスペクタから起動する。** シーン内の VRCAVProVideoPlayer 構成を検出し、「検出結果一覧 → チェックして実行」形式で以下を一括実行する:

- 各 VRCAVProVideoScreen → 2 の AunCastScreen 変換
- 各スピーカー → 2 の AunCastSpeaker 変換（6 の検知・警告込み）
- 旧プレイヤーの撤去は自動化しない。**残存する旧コンポーネント・オブジェクトを一覧提示し、ユーザーの手で削除するよう案内するにとどめる**（EditorOnly 化の自動適用はプレハブ構成・参照関係次第で単純にできないため行わない）。

トンネル構成は 6 の検知ロジックを流用する。URL の自動転写は不要と判断し実装しない（配信 URL はスタッフパネルへの入力／`defaultUrl` の手動設定で運用する）。

### 6.【対応決定】スピーカーセットアップの改善: 責任境界の一般化とトンネル構成対応

トンネル機構（AudioMixTunnel）の AunCast 本体への導入は検討の結果**見送り**（後述）。セットアップ改善は以下の方針で行う。

**責任境界（一般化の原則)**: AunCast が面倒を見るのは **VRCAVProVideoSpeaker が付いた AudioSource（= AVPro 音声のシンク）まで**。その先の音声パイプライン（トンネルの出力先、外部音量制御コンポーネント等）には関知しない。問題の本質は「シンクが A/B の 2 系統に複製されるのに対し、シンクを入力に取る外部コンポーネントが単一入力しか受けられない」ことにあり、対応はシンク境界で完結させる。

**変換候補の検出（3 の一覧に対する要件）**:
- 不可聴設定（カスタムロールオフ全キー 0・volume 0・無効化・非アクティブ）でも VRCAVProVideoSpeaker 付きなら**変換候補一覧に必ず載せ、自動除外しない**。不可聴シンクは「トンネル給音用ダミー」として機能上必須のことがある。変換するかはユーザーがチェックボックスで選択し、状態（不可聴・無効・非アクティブ）をラベル表示する。
- トンネル検知時は、ダミーシンクに対して「変換ではなく互換トンネルフロー（後述。ダミーシンクは削除するだけでよい）」を案内する。

**シンクへの外部参照のスキャンと警告（自動差し替えはしない)**:
- 変換対象シンクを参照している外部コンポーネントを、シーン全体（UdonBehaviour の publicVariables、UdonSharpBehaviour / MonoBehaviour のシリアライズフィールドを SerializedProperty 走査）から検索し、一覧で警告表示する。
- 警告内容: AunCast ではシンクが A/B の 2 系統に分かれるため、**単一の AudioSource 入力しか受けない参照元は、A/B 両系統への複数対応（またはクロスフェードを断念して Active 側へ動的に切り替える改修）をしない限り移行できない**。内容が不明なワールド独自の Udon コンポーネントに対して AunCast ができるのはこの警告までで、自動差し替え・自動改修は行わない。
- 誤警報の抑制: 旧プレイヤー本体が自分のスピーカーを参照しているだけのケース（iwaSync3 / VizVid / USharpVideo で確認した共通パターン）は旧プレイヤーごと削除されるため、参照元がシンクと同一プレハブ/階層内にある場合はその旨をラベルで区別表示する。

**AudioOutputTunnel 複数対応版（AunCastAudioOutputTunnel）の同梱提供**:
- TopazChatPlayer は利用者が多いため、付属の AudioOutputTunnel に限り **A/B 2 入力対応（またはクロスフェードなし前提で Active 側へ動的切替する）互換トンネル**を AunCast に同梱し、型名 `AudioOutputTunnel`（+ `input`/`leftOutput`/`rightOutput`/`stereoOutput` の変数構成ヒューリスティック）で検知した際に差し替えを案内する。名称は他の AunCast 系コンポーネントと同様に **AunCast プレフィックスを付けて `AunCastAudioOutputTunnel`** とする。
- 互換トンネルの A/B 入力には **AunCast 内蔵の PlayerA/B シンク AudioSource（AunCastSpeaker 付き既定スピーカー）**を再配線が配線する。旧トンネルのダミーシンクは**削除するだけでよく、変換も複製も不要**。
- **内蔵シンクの不可聴化（実装済み）**: トンネルが存在する場合、再配線がトンネル入力シンクを不可聴化する。`GetOutputData` は **AudioSource.volume の影響を受ける（検証済み）** ため、トンネルが読む信号を消さないよう volume は触らず、`spatialBlend = 1`（3D）＋ カスタムロールオフを全域 0 にして直接音のみを消す（参考実装と同じカスタムロールオフ手法）。冪等。
- **A/B ミックス（実装済み）**: `GetOutputData` が volume 反映済みのため、トンネルは A+B を単純加算するだけで fadeGain（Standby ミュート・クロスフェード）が自然に反映される。Active 側選択の内部配線は不要。
- **再生安定性（実装済み）**: DSP クロック（`AudioSettings.dspTime`）に同期して書き込み、フレーム落ちで遅延しすぎた場合や書込ヘッドが再生ヘッドへ追いつく場合はバッファをリセットして復帰する（参考実装 TopazChat AudioOutputTunnel と同じリングバッファ手法）。
- **遅延増加の警告**: AunCastAudioOutputTunnel を利用する構成では、直結出力に比べて音声遅延（リングバッファ分 ≒ 数十 ms〜）が増える旨をセットアップ時に警告する。元の AudioOutputTunnel 構成でも同等の遅延はあったため移行で悪化するわけではないが、直結構成への切り替えという選択肢があることを利用者が判断できるようにする。
- これにより「+ Reverb Filter」型ワールドはトンネルから先（リバーブ・外部音量制御・出力スピーカー）を**無傷のまま**移行できる。
- 残る実測項目: ユーザー音量スライダーの効き（内蔵シンク経由で volume 管理されるため反映される想定）と、映像に対する音声の再生位置ギャップ（リングバッファ分 ≒ 数十 ms〜）を実機で確認する。

**実装ポイント**: 変換候補の検出（不可聴・無効も列挙 + 状態ラベル）、外部参照スキャン、AudioOutputTunnel 検知と互換トンネル差し替え案内、互換トンネル本体（UdonSharp。`.cs` + `.asset` ペア作成の規約に従う）。既存の `CollectSpeakerCandidates` / `ExecuteSpeakerSetup`（複製方式）は 2 の宣言型モデルに置き換える。

## 見送り事項の記録

**トンネル式音声出力レイヤー（AudioMixTunnel）の本体導入 — 2026-07-04 見送り決定。** A/B 内蔵シンク → PCM ミキシング → 素の AudioSource 出力という構成は、ユーザー調整対象を最少 1 個にでき移行も容易になるが、リングバッファ遅延（参考実装の 4096 サンプル ≒ 48kHz で約 85ms）とメインスレッド Update 駆動ゆえのフレームヒッチ時の音切れが、AunCast の「無音で切り替える」思想と相反するため。トンネルが必要なケースは 6 の互換トンネル（AudioOutputTunnel 検知時のみ）で限定的に対応する。

**URL プリセットリスト — 2026-07-04 不要と判断。** メイン + 予備 URL の切替機構（AunCastSettings のプリセット配列 + StaffControlPanel の選択 UI）を検討したが、採用しない。予備 URL への切替は `defaultUrl` とスタッフパネルへの手動入力で運用する。
