# VRChat/Udon 開発上の注意点

## 1. 目的
- VRChat World SDK + UdonSharp 開発でハマりやすいポイントを事前に避ける。
- 実装時の判断基準を統一し、再発トラブルを減らす。

## 2. Udon API 露出制約
- C# では使えても Udon で未露出の API がある。
- エラー例:
  - `Method is not exposed to Udon: 'new VRCUrl(...)'`
  - `Method is not exposed to Udon: 'VRCUrlInputField.text'`
  - `Method is not exposed to Udon: 'btn.onClick.Invoke()'`
- 対応方針:
  - URL は `VRCUrlInputField.GetUrl()` 経由で取得する。
  - `VRCUrl` の文字列直接生成に依存しない。
  - `Button.onClick.Invoke()` は Udon 未露出。ボタン押下のプログラム的発火は `UdonBehaviour.SendCustomEvent(eventName)` で代替する。
  - Udon 側イベントから呼ぶメソッドは最小 API セットで構成する。

## 3. ワールド空間 UI の必須構成
- Canvas ルートに以下を揃える:
  - `Canvas` (World Space)
  - `GraphicRaycaster`
  - `VRCUiShape`
  - `UI` レイヤー
- クリック不能時の確認:
  1. `VRCUiShape` が Canvas ルートにあるか
  2. レイヤーが `UI` か
  3. 子オブジェクトが別レイヤーに逸脱していないか

### 3.1 VRCUiShape とコライダーの関係
- `VRCUiShape` は **ランタイムで非 Trigger の BoxCollider を自動追加する** ため、Canvas を Player の歩行路付近（特に頭部追従する手元メニュー）に置くと歩行で衝突するようになる。
- 既にコライダーがあれば VRCUiShape は新規追加しないので、**先に自分で BoxCollider を `IsTrigger=true` で置く** と自動追加を抑止できる。
- `IsTrigger=true` でも VRChat のポインタ Raycast には反応する一方、Unity の `CharacterController` は Trigger を素通りするので歩行を邪魔しない。USharpVideo の `AllowFocusView` 付きパネルも同構成（Default レイヤー + Trigger BoxCollider）で動作している。
- **`UiMenu` レイヤーは world UI 用ではない**。名前に反して VRChat 内部メニュー用で、world 側で使うと VRChat のポインタ Raycast マスクで弾かれ操作不能になる。歩行衝突を避けたいだけなら上記 Trigger 方式で解決する。

## 4. EventSystem / InputModule の整合
- シーン内で有効な `EventSystem` は 1 つにする。
  - 複数あると `There can be only one active Event System.` が出る。
- 新 Input System 環境では `StandaloneInputModule` が動かない場合がある。
  - `InputSystemUIInputModule` を使用する。
  - `StandaloneInputModule` の Inspector には `Replace with InputSystemUIInputModule` ボタンがあり手動置換できる。セットアップスクリプトもこれと同等の自動置換を行う。
- セットアップスクリプトで自動整合する場合も、実シーンの Inspector で最終確認する。

## 5. Raycast の干渉対策
- ボタン内のラベル文字や placeholder が raycast を奪うことがある。
- `TextMeshProUGUI` / `Text` は、操作対象でない限り `raycastTarget = false` にする。
- クリック対象は `Button` 本体（`Image`）側へ集約する。

## 6. フォント運用
- Unity バージョン差分で built-in フォント名が変わることがある。
  - `Arial.ttf` ではなく `LegacyRuntime.ttf` を使う。
- TMP の `defaultFontAsset` が `Empty SDF for Default Font` だと警告や表示不整合の原因になる。
- 運用ルール:
  1. `TextMesh Pro VRC Fallback Font JP` を最優先
  2. 見つからなければ `VRC Fallback Font JP` / `VRC Fallback`
  3. 最後に `LiberationSans SDF`

## 7. セットアップスクリプト設計方針
- 既存オブジェクトを即削除しない（失敗時に復旧できるようにする）。
- 参照配線が抜けたときに検出できるログを残す。
- 失敗してもシーンを壊さない（バックアップ復元を優先）。
- UdonSharp Refresh を自動実行する際のメニュー名は環境差がある:
  - 旧: `Tools > UdonSharp > Refresh All UdonSharp Programs`
  - 現行 SDK: `VRChat SDK/Udon Sharp/Refresh All UdonSharp Assets`
  - `UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync()` を直接呼ぶ。public static メソッドなのでリフレクション不要。
  - **メニュー経由の Refresh は非同期のため、直後に `CopyProxyToUdon` を呼ぶと未コンパイルエラーになる。** `CompileSync` は同期実行なのでこの問題が起きない。

## 8. メニュー追従 UI（Viewer 系）
- `OnMenuOpened` / `OnMenuClosed` で表示制御し、非表示時は更新停止。
- 位置・向きは `menuOffset` を Inspector で調整可能にする。
- 「常時表示」ではなく「メニュー開時のみ表示」を基本にする。

## 9. VRChat ランタイム挙動

### 9.1 `Networking.GetServerTimeInSeconds()` / `GetServerTimeInMilliseconds()` の負値
- ローカルテスト（Build & Run）では**負の値**を返すことがある。`[UdonSynced]` float のデフォルト値 `0` と比較すると常に `serverTime < 0` となり、条件が破綻する。
- 対策: 絶対値の比較（`serverTime < threshold`）ではなく、**差分ベースの比較**（`threshold - serverTime` が妥当な範囲内か）で判定する。同じ `GetServerTime()` 由来の値同士の差分は正しく計算される。

### 9.2 AVPro `GetTime()` の起動直後挙動
- `OnVideoStart` 直後は `GetTime()` が **1〜2 秒ほど `0` を返し続ける**ことがある（特にライブストリーム）。そのまま stall タイマーで計測すると誤 Resync が発火する。
- さらに、前のロードで進んでいた**残留値**を一瞬返してから `0` に巻き戻る、あるいは非常に小さい値を返したまま滞留する、というケースも観測される。
- 対策: 「`GetTime() > 0` を 1 度でも観測したか」を起動判定に使わない。**前進 delta（`current - last > minAdvanceThresholdSec`）を観測した瞬間**を起動判定トリガーにする。これなら残留値・巻き戻り・微小値滞留のいずれにも左右されない。
- 参考実装: `LocalDualPlayerController.MonitorActivePlayer` の `_hasSeenPlayerTimeAdvance` フラグ。

### 9.3 バグ調査用ログ
- VRChat のログ: `%AppData%\..\LocalLow\VRChat\VRChat\output_log_*.txt`
- Udon 例外やランタイムの挙動はここに記録される。

### 9.4 SDK Build & Test の複数クライアント起動で `displayName` が衝突する
- Unity SDK の Client Sim / Build & Test から複数クライアントを同時起動すると、全員が同じアカウントで入るため `VRCPlayerApi.displayName` が衝突する。
- `displayName` ベースのアクセス制御（スタッフリストなど）を使っていると、その名前に一致した全員が権限を得てしまい、テスト中の挙動が本番と乖離する。
- 対策: 衝突時は `playerId` でタイブレークする（通常は最小 `playerId` のみを採用）。本番 VRChat では起きない異常なので、名前一致者が複数いるときだけ働く軽い追加ロジックで十分。

### 9.5 `VRCAVProVideoPlayer.AutoPlay` の意味論
- `AutoPlay=true` は「Start 時に自動再生」ではなく「`LoadURL` 完了後に自動再生」の意味。
- URL が未設定のままでは Start 時点で内部的な `PlayURL` は呼ばれず、`OnVideoStart` / `OnVideoReady` も発火しない。**空 URL + AutoPlay=true は「何もしない」が正しい挙動**（Editor ランタイム・VRChat 本番ともに同じ）。
- 影響: URL を Udon 側から `LoadURL` で動的注入する設計では、`AutoPlay` の値に依らず初回 `LoadURL` まで状態機械は IDLE のままで良い。
- テスト用スタブ (`AVProVideoStub` など) を使う場合は、スタブが「Start 時に自動ロード」する実装になっていないか確認する。乖離していると「Editor では再生中なのに状態は IDLE のまま」のような症状で本番との挙動差が現れる。

### 9.6 `OnAudioFilterRead` と Udon VM ヒープは完全に分離されている
- **メイン→オーディオ**: メインスレッドから UdonSharpBehaviour の public フィールド（例: `fadeGain`）を書き換えても、`OnAudioFilterRead`（オーディオスレッド）の読み出しが**古い値のままになる**。
- **オーディオ→メイン**: 逆方向も同様。`OnAudioFilterRead` 内でフィールドに書き込んだ値（スカラー・配列いずれも）は、メインスレッドの Udon VM コードからは**一切読めない**。VRChat 本番環境・ClientSim の両方で確認済み。
- 原因: `OnAudioFilterRead` は `OnAudioFilterReadProxy` 経由で `UdonBehaviour.RunEvent()` に転送されるが、C# プロキシのフィールドと Udon VM ヒープは別メモリ空間。配列（参照型）を使ってもオブジェクト参照自体が異なるため共有できない。さらに ClientSim では Harmony パッチにより `OnAudioFilterRead` の実行自体がブロックされる。
- 対策:
  - **メイン→オーディオのパラメータ注入**: Unity ネイティブ API（`AudioSource.volume`、`AudioSource.mute` 等）を使う。
  - **オーディオ→メインのデータ取得**: `AudioSource.GetOutputData()` をメインスレッドから呼び、フィルタ適用後の PCM データを取得する。AudioLink も同じ手法を使用している。
  - `OnAudioFilterRead` 内でやるべきは遅延バッファのような「`data[]` の書き換えだけで完結する処理」に限定する。

### 9.7 `BaseVRCVideoPlayer.IsPlaying` は接続完了を意味しない
- `LoadURL()` を呼ぶと `IsPlaying` は**即座に true を返し始める**が、実際の再生（`GetTime()` が進行し始める瞬間）までには数秒〜十数秒の接続フェーズがある。
- 症状: `IsPlaying && (serverTime - anchorServerTime)` で再生経過時間を計算すると、接続フェーズ分が誤って積算され、Gap 検知や進捗表示がズレる。
- 対策: 実再生のアンカーは **`IsPlaying` ではなく `GetTime() > 0`（または 9.2 の前進 delta 観測）を基準に取る**。「再生開始」と「接続開始」は別イベントとして区別する。
- 参考実装: `SyncDebugDisplay.PostLateUpdate` の `_hasPlayStartServerTime` アンカー取得ロジック。

### 9.8 グループロール API は Udon 未露出
- VRChat のグループロール（`GroupRole`）を Udon から取得する公式 API は存在しない（2026-04 時点で Canny に要望あり、未実装）。
- 「グループインスタンスで特定ロール保有者のみスタッフ昇格」のような制御は現状できない。
- 代替案:
  - グループインスタンスの種別をワールド制作者が選別（公開/メンバー限定など）して実質的にアクセスを絞る。
  - `displayName` ベースのホワイトリストと、パスコード解錠などのフォールバックを併用する。

### 9.9 `VRCAVProVideoPlayer.Initialize` デリゲートフックのタイミング
- `VRCAVProVideoPlayer.Initialize` は静的デリゲートで、`IAVProVideoPlayerInternal` を返すフックを登録する。ClientSim の NoOp スタブやカスタムスタブがこの仕組みで注入される。
- フックの登録タイミングは `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` で十分間に合う。`VRCAVProVideoPlayer` 側はシーン内の各インスタンスの `Start()` で Initialize を呼ぶため、BeforeSceneLoad まで前倒しする必要はない。
- 注意: ClientSim 自身も BeforeSceneLoad で同じデリゲートに自前スタブを登録する。後勝ちで差し替わるので、カスタムスタブ側は AfterSceneLoad（= ClientSim より後）に登録すれば確実に有効化される。

### 9.10 `IAVProVideoPlayerInternal.Loop` は外部から false がセットされる
- `VRCAVProVideoPlayer` プロキシ側のロジックから、内部実装の `Loop` プロパティに `false` が設定されることがある（ユーザーが `Loop` を有効にしていなくても）。
- ライブストリーム想定のスタブなど「常にループすべき実装」では、この setter で受け取った値をバックエンド（`AudioSource.loop` / `VideoPlayer.isLooping`）に流してはいけない。値は記録するだけにして、バックエンドは常に `loop = true` のまま維持する。

### 9.11 `AudioSource.Pause` / `UnPause` の最小遅延
- `AudioSource.Pause()` → `UnPause()` の往復には Unity 内部でフレーム〜数フレーム単位のオーバーヘッドが乗る。
- 症状: 100ms 以下の短い一時停止を繰り返すと、指定した持続時間より実際は長く止まり、累積すると数百ms〜の想定外遅延になる。
- 対策: 「短時間のストール」「サブフレームの遅延」を正確に再現したいときは、`Pause`/`UnPause` を使わず**再生を止めないまま別手段で遅延を表現する**:
  - 上位クロックを保持し、毎フレーム `AudioSource.time` を補正する（閾値を設けないとノイズ源になる。9.12 参照）
  - `AudioEchoFilter` で DSP レベルの遅延を付与する（9.14 参照）

### 9.12 `AudioSource.time` のシーク頻度とノイズ
- `AudioSource.time = value` はサンプル境界で再生位置を切り替えるため、毎フレーム無条件に適用すると音声グリッチ（プチノイズ）が乗る。
- 対策: 補正適用前に差分閾値（経験的に 50ms 程度）を設け、閾値未満なら何もしない。これで上位クロックとの微小なズレは放置されるが、聴感上の問題は出にくい。
- 表示用の再生時刻（UI やデバッググラフ）には補正後ではなく**上位クロックの値をそのまま返す**と精度が保てる。

### 9.13 Worker パターンによるオーディオスレッド活用（制約と限界）
- `OnAudioFilterRead` が Udon VM ヒープと分離されている（9.6）制約を逆手に取り、**専用の空 GameObject に UdonBehaviour を載せてオーディオスレッド上で重い計算をオフロードする**手法が存在する（ku6dra 氏「Udon でマルチスレッド」記事）。
- 仕組み:
  1. Worker 用 GameObject に `AudioSource`（clip なし・再生状態）と UdonBehaviour を配置。`OnAudioFilterRead` がオーディオスレッドで毎ブロック呼ばれる。
  2. メインスレッドで入力データを Worker のフィールドにセットし、トリガー用 `bool[1]` 配列の参照をメイン側にコピーしてから Worker を起動。
  3. Worker の `OnAudioFilterRead` 内で重い計算を実行。完了したら `bool[1][0] = true` をセット。
  4. メインスレッドは `SendCustomEventDelayedFrames` でポーリングし、コピーしておいた配列参照経由で完了を検知。結果を読み取る。
- 配列参照のコピーがポイント: メインスレッドが**起動前に**配列参照を自分のローカル変数にコピーしておけば、その参照は C# ヒープ上のオブジェクトを指すため、オーディオスレッドからの書き込みがメインスレッドでも見える。ただし Udon VM フィールド経由のアクセスでは見えない（9.6 のとおり）。
- 制約:
  - **実行中に Worker の Udon フィールドに触れてはならない**。メインスレッドからのフィールド書き換えがオーディオスレッドの実行と競合し、Udon VM がクラッシュする可能性がある。
  - Worker に `OnAudioFilterRead` 以外のイベント（`Update` 等）を持たせない。Udon VM が同一 behaviour で複数イベントを並行実行しようとして破壊的競合を起こす。
  - AudioSource のサンプリングレート（通常 48kHz）と DSP バッファサイズに依存して呼び出し頻度が決まる。VRChat のデフォルト設定では約 10ms 間隔。
  - 長時間（10秒超）ブロックすると Unity のオーディオシステムがタイムアウトする。
- **連続的なリアルタイム監視には不向き**: このパターンは「重い計算を一度だけオフロードして結果を受け取る」バッチ処理に適している。毎フレームの RMS 計測やドリフト追従のような継続的データストリームには、`AudioSource.GetOutputData()` をメインスレッドから呼ぶ方が単純で信頼性が高い（9.6 参照）。

### 9.14 `AudioEchoFilter` による DSP レベルの音声遅延
- Unity ビルトインの `AudioEchoFilter` を使えば、`OnAudioFilterRead` を使わずにオーディオスレッド上で音声遅延を実現できる。VRChat 本番環境で動作確認済み。
- 設定:
  - `delay`: 遅延時間（ms、10〜5000）
  - `decayRatio = 0`: 反復エコーなし（1回だけの純粋な遅延）
  - `dryMix = 0`: 元信号をカット
  - `wetMix = 1`: 遅延信号のみ出力
- この組み合わせで「指定ミリ秒だけ遅延した音声」が得られる。
- メリット:
  - Unity ネイティブコンポーネントとしてオーディオスレッドで動作するため、Udon VM ヒープ分離（9.6）の影響を受けない。
  - `OnAudioFilterRead` のようなカスタム DSP コードが不要。
  - セットアップスクリプトから `AddComponent<AudioEchoFilter>()` で追加するだけで使える。
- Udon からの動的制御: `GetComponent<AudioEchoFilter>()` でのコンポーネント取得、`delay` プロパティの読み書きともに Udon から動作する。VRChat 本番環境で `delay` を毎フレーム動的に変更し、遅延が聴覚上リアルタイムに追従することを確認済み。
- ドリフト吸収への応用: `OnAudioFilterRead` のリングバッファ方式（9.6 で不可能と判明）の代替として、`AudioEchoFilter.delay` をメインスレッドから動的に制御することで、DSP レベルの遅延吸収が実現可能。

## 10. 推奨デバッグ手順
1. Console の Udon 露出エラーを最優先で潰す。
2. UI が押せない場合は `VRCUiShape` / Layer / EventSystem / InputModule を確認。
3. 参照配線（特に URL 入力と placeholder）を Inspector で確認。
4. Setup 実行後に EventSystem が想定どおりか再確認。
5. 再生・同期系の不具合は VRChat の `output_log_*.txt` を確認する。

## 11. スキル化するべきか
- 結論:
  - **このリポジトリにはドキュメントとして残す**（今の `Docs` 追加は正解）。
  - **並行して Codex スキル化すると運用効果が高い**。
- スキル化が有効な理由:
  - 新規タスク開始時に同じチェックリストを毎回適用できる。
  - Setup/配線/UI不具合の再発時に初動が早くなる。
  - 複数ワールドや別プロジェクトへ横展開しやすい。
- 推奨運用:
  - 正本: この `Docs`（プロジェクト固有の事実）
  - 実行手順: スキル（作業時に呼び出すチェックフロー）
