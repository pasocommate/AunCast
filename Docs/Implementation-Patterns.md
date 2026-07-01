# AunCast 実装パターン

プロジェクト内で繰り返し現れる、VRChat/Udon 上での同期変数・スタッフ権限操作・UI 双方向追従の設計ルールをまとめる。
VRChat/Udon API レベルの一般的な注意点は `VRChat-Udon-Development-Notes.md` を参照。

## 1. Manual sync + `AsStaff` メソッドパターン

`LocalDualPlayerController` は `BehaviourSyncMode.Manual`。スタッフ UI からの操作は
「ローカル直呼び」ではなく**専用の `SetXxxAsStaff` メソッド経由**で必ず同期する。

既存例: `PlayVideoAsStaff` / `StopVideoAsStaff`

> **注意**: 音量 (`_localVolume`) と Silence Resync (`_autoSilenceResyncEnabled`) は
> 各クライアントのローカル設定に変更済み。同期不要のため `AsStaff` パターンではなく、
> `SetVolumeLocal` / `SetAutoSilenceResyncEnabled` でローカル値を直接書き換える。
> UI は UserStatusPanel 側に配置し、スタッフ権限チェックなしで全ユーザーが操作可能。
>
> **注意（ロック機構）**: 旧設計の `SetLockedAsStaff` 系（同期 lock フラグ）は廃止され、
> StaffControlPanel のアクセス制御は「許可ユーザー名リスト + WallControlPanel
> 経由のローカルパスコード解錠」に置き換えられている（同期なし）。

補足:
- Staff 操作は `AsStaff` 版 API に一本化する。
- `PlayVideo` / `StopVideo` のような非 Staff 版 API は廃止し、呼び出し口を増やさない。

### 実装テンプレート

```csharp
[PublicAPI]
public void SetXxxAsStaff(Tx value)
{
    // 1. 入力の正規化
    value = Normalize(value);

    // 2. Owner 移譲（非 Owner が呼んでも動くようにする）
    if (!Networking.IsOwner(gameObject))
        Networking.SetOwner(Networking.LocalPlayer, gameObject);

    // 3. 同期変数を書き換え
    _syncedXxx = value;

    // 4. ローカル側にも即時反映（Owner は OnDeserialization が呼ばれないので必須）
    ApplyXxxLocal(value);

    // 5. Manual sync を要求
    QueueSerialize();
}
```

### 呼び出し側 (UI) のルール

- `StaffControlPanel` 側のイベントハンドラは **冒頭で `_isStaff` チェック**を入れる。
  非スタッフが UI を操作した場合はアクセス拒否表示で UI を同期値へ戻す。
- 操作可能な UI は `interactable = _isStaff` に設定し、
  非スタッフが物理的に触れないようにもする（アクセス権チェックとの二重防御）。

## 2. `OnDeserialization` での同期反映

Manual sync の受信側は、非 Owner 向けに `OnDeserialization` で
「同期変数が現在のローカル状態と異なれば反映する」差分検知を書く。

```csharp
public override void OnDeserialization()
{
    if (Networking.IsOwner(gameObject)) return;

    // 値型の変更検知: float は Mathf.Approximately で比較（== は浮動小数誤差に弱い）
    if (!Mathf.Approximately(_syncedXxx, GetXxxLocal()))
    {
        ApplyXxxLocal(_syncedXxx);
    }

    // bool / int の変更検知: 直前値を保持する補助フィールドで比較
    if (_syncedBool != _lastSyncedBool)
    {
        _lastSyncedBool = _syncedBool;
        ApplyBoolLocal(_syncedBool);
    }
}
```

### Late Joiner 対応

`OnPlayerJoined(VRCPlayerApi player)` 内で新規参加者が来たら Owner 側から
`QueueSerialize()` を呼び、最新の同期変数が届くようにする。既存実装あり。

## 3. `[UdonSynced]` 初期値の扱い

- `[UdonSynced] private float _syncedXxx = 0.6f;` のフィールド初期化子は
  **全クライアントで同じデフォルト値から開始する**。
- 非 Owner は初回同期で Owner 側の値を受信し上書きする。
- 起動時初期化 (`Start`) は Owner のみが inspector パラメータ (`defaultXxx` 等) を
  同期変数へ書き込み、全員が `ApplyXxxLocal(_syncedXxx)` を呼ぶ形にする。

```csharp
// Start 内の例
if (Networking.IsOwner(gameObject))
    _syncedXxx = defaultXxx;          // Owner のみ inspector 値を反映
ApplyXxxLocal(_syncedXxx);             // 全員がローカル適用（非 Owner は初回 deserialize で上書きされる）
```

## 4. UI の双方向追従パターン

Slider / Toggle など **同期値を操作する UI** は、次の 3 つを**同じ Update ポーリング**で処理する:

1. ローカルユーザーの操作検知 (`slider.value != _lastSliderValue`)
2. 他クライアント (他スタッフ) からの同期反映検知 (`controller.GetXxx() != slider.value`)
3. 初回フレームの `_lastSliderValue` 初期化

> **注意**: ローカル専用値（音量・無音 Resync トグル等）を操作する UI はステップ 2 が不要。
> `UserStatusPanel.PollVolumeSlider` のように初期化 + 操作検知のみの簡略版を使う。

```csharp
private void PollXxxSlider()
{
    if (xxxSlider == null) return;

    // 初回: 初期値をキャプチャして抜ける
    if (!_xxxSliderInitialized)
    {
        _lastXxxSliderValue = xxxSlider.value;
        _xxxSliderInitialized = true;
        return;
    }

    // ユーザー操作で動いた
    if (!Mathf.Approximately(xxxSlider.value, _lastXxxSliderValue))
    {
        OnXxxSliderChanged();   // _isStaff チェック付きハンドラ
        return;
    }

    // ユーザー操作以外（他スタッフの同期反映）— UI を同期値に追従
    if (controller != null)
    {
        float synced = controller.GetXxx();
        if (!Mathf.Approximately(synced, xxxSlider.value))
        {
            xxxSlider.value = synced;
            _lastXxxSliderValue = synced;
        }
    }
}
```

**なぜこの順序か**: ユーザー操作を先に処理しないと、
「スタッフが動かした直後の 1 フレームで同期値がまだ古く、UI を戻してしまう」
フラッシュバック現象が起きる。

### エディタ時の表示プレビュー同期

`AunCastSettings` の値を変更したら、ランタイムフィールドへの転写
（`ApplyXxxSettingsToScene`）と同時に、その値を表示する**素の UI コンポーネント**
（TMP 数値表示/入力欄・`Slider`・`Toggle`）へも反映し、Play せずともシーン上の見た目を
実値に揃える。これらは runtime では `Start` / `OnDeserialization` で初期化されるため、
未反映だとエディタ上で旧値が見えてしまう。

- **参照取得は backing UdonBehaviour の `publicVariables` を優先する**（取れなければ
  プロキシの `SerializedObject.FindProperty` へフォールバック）。`UserStatusPanel` /
  `StaffControlPanel` は `AunCast.prefab` 直下なのでプロキシの SerializeField でも解決できるが、
  ネストプレハブ（`AunCast.prefab` 内の `WallControlPanel` 等）はプロキシの参照が
  編集時に null へ解決されることがある。実行時が参照を解決するのと同じ
  `GetBackingUdonBehaviour(panel).publicVariables.TryGetVariableValue(fieldName, out var v)`
  で読めばネストの有無に関わらず確実。
- 値の書き込み: `TMP_Text` は `.text`、`TMP_InputField` は `SetTextWithoutNotify`、
  `Slider` は `SetValueWithoutNotify` で更新（`onValueChanged` を発火させない）。更新後は
  `EditorUtility.SetDirty` + `PrefabUtility.RecordPrefabInstancePropertyModifications`。
- TMP テキスト等は値を変えても編集時に自動再描画されないことがあるため、変更があったら
  `InternalEditorUtility.RepaintAllViews()`（`RepaintUiViews`）で明示再描画する。
- 実装は `AunCastSettingsInspector.SyncTextDisplay` / `SyncInputField` / `SyncSlider` と、
  参照取得の `GetReferencedUiComponent` / 再描画の `RepaintUiViews`。

> **対象外（ジェスチャートグル）**: ウォールパネルの呼び出しジェスチャートグルは編集時
> プレビュー同期の対象にしていない。チェックマークがカスタム Graphic（UIPanel シェーダー系）で
> 編集時にライブ再描画されにくく、かつ実行時は `WallControlPanel.SyncGestureToggles` が
> `summonGesture` から `isOn` を上書きするため、トグルのシリアライズ値は実行時には使われない。
> 設定値そのものは `ApplyUiSettingsToScene` の `summonGesture` / `desktopSummonGesture` 転写で
> 正しく反映される。

## 5. UdonSynced 変更時の `.asset` 更新

`[UdonSynced]` フィールドを追加 / 改名した後は、
**`Tools > UdonSharp > Refresh All UdonSharp Programs` を手動実行**して、
`LocalDualPlayerController.asset` 等の UdonSharp プログラムアセットに
新しいシリアライズ情報を反映させる。自動ビルドだけでは反映されないことがある。

## 6. `[NetworkCallable]` パラメータ付きイベントパターン (SDK 3.10.2+)

ResyncCoordinator の Owner-Centric モデルで使用。
クライアントが ownership を取らず、Owner に状態変更を依頼するパターン。

### namespace と属性

```csharp
using VRC.SDK3.UdonNetworkCalling;

// 受信側: Owner のクラスに定義
[NetworkCallable]
public void OnResyncRequest(int slotIndex)
{
    if (!Networking.IsOwner(gameObject)) return;
    // 状態変更...
    MarkDirty();
}
```

### 送信側

```csharp
using VRC.SDK3.UdonNetworkCalling;

// クライアントから Owner へ
coordinator.SendCustomNetworkEvent(
    NetworkEventTarget.Owner, "OnResyncRequest", _mySlotIndex);
```

### `[NetworkCallable]` メソッドの制約

- `public void` のみ（戻り値不可）
- パラメータ最大 8 個
- デフォルト引数不可、オーバーロード不可、ref/out/params 不可
- `BehaviourSyncMode.Manual` または `NoVariableSync` で利用可（`None` は不可）
- **デリバリー保証なし**（fire-and-forget）

### 遅延シリアライズ (`MarkDirty`) パターン

複数の `[NetworkCallable]` が同一フレーム内に到着する場合に備え、
`RequestSerialization()` を直接呼ばず `MarkDirty()` でフラグを立て、
`Update()` で 1 回だけまとめて送信する。

```csharp
private bool _serializationPending;
private void MarkDirty() { _serializationPending = true; }

private void Update()
{
    if (!Networking.IsOwner(gameObject)) return;
    if (_serializationPending)
    {
        // 必要なら圧縮等の前処理
        RequestSerialization();
        _serializationPending = false;
    }
    // ...
}
```

### クライアント側リトライポーリングパターン

イベントロスト対策として、送信後に同期変数をポーリングし、
Owner 側の状態が変わっていなければ一定間隔で再送する。

```csharp
// STATE_REQUEST_PENDING 中に Owner 側が STATE_NONE のまま 3 秒経過 → 再送
if (coordinator.GetResyncState(_mySlotIndex) == ResyncCoordinator.STATE_NONE
    && (now - _lastResyncRequestSentAt) >= RESYNC_REQUEST_RETRY_SEC)
{
    coordinator.SendCustomNetworkEvent(
        NetworkEventTarget.Owner, "OnResyncRequest", _mySlotIndex);
    _lastResyncRequestSentAt = now;
}
```

### ownership ベースとの使い分け

| 用途 | 方式 | 理由 |
|---|---|---|
| クライアント→Coordinator の状態変更 | `SendCustomNetworkEvent` + `[NetworkCallable]` | 競合排除、パケット削減 |
| スタッフ操作（Global Resync 等） | `TryTakeOwnership` → 直接書換 | 全スロットの原子的更新が必要 |

## 6. ownership 分離オブジェクトの退室クリーンアップ

複数の `[UdonSynced]` オブジェクトを意図的に分離してある場合（例: `ResyncCoordinator` と
`PlaybackMonitor`）、各オブジェクトの ownership は独立に移動する。スタッフ操作で
片方の owner だけが変わったり、マスター離脱で別々のクライアントへ移譲されたりして
ownership が乖離した状況で `OnPlayerLeft` をやらせると、**「片方の所有者が他方の同期変数を書き換える」呼び出しが silent fail する**（`RequestSerialization()` は非所有者だと no-op）。

そのため、**各オブジェクトの「自分の同期変数」は自オブジェクトの所有者だけが
`OnPlayerLeft` で掃除する**。他オブジェクトから命じない。

PlaybackMonitor の例: 自前で全スロット走査し、`coordinator.GetUserPlayerId(i)` の
プレイヤーが `VRCPlayerApi.GetPlayerById(pid).IsValid() == false` のスロットを
3 配列まとめてクリアする。`pid == 0`（Coordinator 側で既に解放済み）も「不在」と扱う。

```csharp
public override void OnPlayerLeft(VRCPlayerApi player) { CleanupStaleSlots(); }
public override void OnPlayerJoined(VRCPlayerApi player) { CleanupStaleSlots(); }

private bool CleanupStaleSlots()
{
    if (!Networking.IsOwner(gameObject)) return false;
    if (coordinator == null) return false;

    bool anyChanged = false;
    for (int i = 0; i < MAX_PLAYERS; i++)
    {
        if (!HasAnyBit(i)) continue;
        int pid = coordinator.GetUserPlayerId(i);
        VRCPlayerApi p = pid == 0 ? null : VRCPlayerApi.GetPlayerById(pid);
        if (p != null && p.IsValid()) continue;
        anyChanged |= ClearAllBitsForSlot(i);
    }
    if (!anyChanged) return false;
    _serializationPending = true;
    FlushSerialization();   // Rejoin 等のロスト対策
    return true;
}
```

ポイント:
- **走査ベースで「現在 invalid なスロット」をまとめて掃除する** ことで、`OnPlayerLeft`
  間の同一クライアント上の実行順レース（Coordinator が先に `userPlayerId[i] = 0`
  にしても、`GetPlayerById(0)==null` で同じ結論になる）を回避できる
- `OnPlayerJoined` でも同じ走査を呼び、`OnPlayerLeft` のシリアライズロスト時の
  フォールバックにする
- 自オブジェクトの掃除以外は他オブジェクトに任せる（責務分離）
- PlaybackMonitor の人数サマリは、ビット配列全体ではなく
  `coordinator.GetUserPlayerId(i) != 0` の割当済みスロットだけを数える。
  未割当スロットの残留ビットを混ぜると、インジケーターに `■` が無いのに
  `Playing` だけが 1 以上で残る表示不整合が起きる。
- クライアントがスロット割当を初めて検出した直後は、PlaybackMonitor へ
  Playing / Connecting / Error の 3 状態を明示的に初期報告する。
  退室直後のスロットが同じ `OnPlayerJoined` 内で再利用されると、割当済み判定だけでは
  以前の利用者が残したビットと新しい利用者の状態を区別できない。

## 7. N 個配置 subscriber 群への AunCastEventBus 配信

シーン内に複数配置されうる `VideoMeshScreen` / `VideoUiScreen` / `WallControlPanel`
のような購読者へ publisher から通知するときは、publisher 側に具象型配列を持たせず
`AunCastEventBus` 経由で配信する。

ルール:
- Bus は backing `UdonBehaviour[]` の subscriber 配列だけを持ち、具象型を知らない
- 配信は `SendCustomEvent(eventName)` で行い、イベント名は `AunCastEventBus` の
  `public const string` に集約する
- `SendCustomEvent` で渡せない bool / enum などの離散値は、値ごとにイベントを分ける
  （例: `OnPortablePanelShown`）
- Texture のように値そのものが必要な場合だけ、Bus 上の
  `[System.NonSerialized] public` フィールドへ格納し、subscriber が pull する
- `AunCastSettingsInspector` の再配線処理だけが subscriber の具象型を集め、backing
  `UdonBehaviour` に変換してから
  Bus 配列と各 `eventBus` 参照を `SerializedObject` / `FindProperty` 経由で設定する

### プレハブ運用と自動再配線

`WallControlPanel.prefab` などの prefab に `eventBus` フィールドを焼き込むことは
シーン依存のため不可能。新規 prefab をシーンに配置した直後は `eventBus = null`
だが、以下の経路で配線が反映される:

1. **手動**: `AunCastSettings` Inspector の **「AunCastEventBus 参照を再配線」
   ボタン** を押下
2. **Play モード遷移時** (`AunCastAutoRewire`): `playModeStateChanged` の
   `ExitingEditMode` で開いている全シーンに対して再配線
3. **ビルド・アップロード時** (`AunCastBuildCallback`): `IProcessSceneWithReport`
   で VRC SDK のシーンビルド処理直前に再配線

`SetObjectProperty` / `SetObjectArrayProperty` の差分検知により、配線が既に最新の
場合は no-op になるので、これらの自動経路がユーザーの手動編集を無闇に上書きする
ことはない。ただし subscriber 配列をシーン内全件より少なく絞った手動編集は
**毎回シーン内全件に戻される** ことに注意（バスの意味論として「シーン内 subscriber
全件に配信する」を維持しているため）。

## 8. 2 つの behaviour が相互参照する循環の片方向化

2 つの UdonSharpBehaviour が互いを具象型フィールドで参照し合うと、型レベルの循環
（ループ参照）になる。これは UdonSharp ではコンパイル/初期化順序の問題は起こさない
（Inspector 配線のコンポーネント参照に過ぎない）が、結合度が上がりテスト・再利用・
変更波及の面で不利になる。`AunCastEventBus`（§7）が複数購読者向けなのに対し、
**1 対 1 の相互参照**はこのパターンで片方向化する。Core→UI の `staffNotifyTarget`
や UI↔UI の `UserStatusPanel`↔`StaffControlPanel` がこの形。

ルール:
- **型循環は片辺だけ基底型化すれば切れる。** 両辺を基底型にする必要はない。
  従属側（通知/命令を送るだけの辺）のフィールドを `StaffControlPanel` のような
  具象型から `UdonSharpBehaviour` 基底型に変える。残した具象辺が「状態の所有者」になる。
- **基底型辺は `SendCustomEvent(メソッド名)` で呼ぶ。** 引数なしの通知・命令のみ
  送れる。呼び先メソッドは `public` であること。
- **bool / enum などの値は、具象のまま残した逆辺から push してキャッシュする。**
  基底型辺では値を渡せず（§7 のとおり `SetProgramVariable` 多用や「値ごとにイベント
  分割」は避けたい）、クエリ（戻り値あり）も `SendCustomEvent` では呼べないため。
  例: 解錠状態は所有者の `StaffControlPanel` が `viewerStatusPanel.SetStaffUnlocked(bool)`
  （具象呼び出し）で `UserStatusPanel` に push し、UI 側は `_staffUnlocked` を読む。
- **状態を変える箇所すべてで push する。** キャッシュ化前にライブクエリで成立して
  いた経路（例: `OnPlayerJoined` の許可ユーザー自動解錠）を見落とすと、キャッシュが
  stale になる。所有者側の状態変更点を網羅して push を入れる。
- 既存の `staffNotifyTarget`（`LocalDualPlayerController` / `ResyncCoordinator` /
  `PlaybackMonitor`）と同じ形。新規の 1 対 1 相互参照を作りそうになったら、まず
  どちらを所有者にするか決め、従属辺を基底型 + `SendCustomEvent` にする。
- 基底型化したフィールドはシーン/プレハブの参照が外れる場合があるため、変更後は
  `Tools > UdonSharp > Refresh All UdonSharp Programs` を実行し、Inspector で配線を
  確認・必要なら再アサインする。

## 9. PlayerData 永続化パターン

VRChat の `PlayerData` API を使い、ローカル設定をワールド再参加後も復元する。

### キー命名規則

`AunCast-PascalCase` 形式（例: `AunCast-Volume`, `AunCast-VrGesture`）。

### 保存・復元の流れ

1. **保存**: 設定変更時に `PlayerData.SetFloat(KEY, value)` / `SetInt` で書き込む。
2. **復元**: `OnPlayerRestored(VRCPlayerApi player)` で `PlayerData.HasKey` →
   `GetFloat` / `GetInt` で読み出し、ローカル状態に適用する。
3. **UI 再同期**: `OnPlayerRestored` は全 `UdonSharpBehaviour` で同一フレーム内に
   発火するため、コンポーネント間の実行順は保証されない。復元された値を UI に
   反映するには `_pending` フラグを立て、**次フレームの `Update()`** で消費する。

### 現在の永続化項目

| キー | 型 | 保存元 | 復元先 |
|------|----|--------|--------|
| `AunCast-Volume` | float | `LocalDualPlayerController.SetVolumeLocal` | `LocalDualPlayerController.OnPlayerRestored` |
| `AunCast-VrGesture` | int | `UserStatusPanel.SetSummonGestureFlag` | `UserStatusPanel.OnPlayerRestored` |
| `AunCast-DesktopGesture` | int | `UserStatusPanel.SetDesktopSummonGestureFlag` | `UserStatusPanel.OnPlayerRestored` |

### 制約

- `VRCUrl` は Udon ランタイムで `new VRCUrl(string)` が使えないため、
  PlayerData に文字列保存しても VRCUrl に復元できない
  （`VRChat-Udon-Development-Notes.md` §1 参照）。

## 10. エディタ設定のプロジェクト単位永続化パターン

ワールド制作者向けのエディタ状態（利用規約への同意など）を **プロジェクト単位** で
保存する場合は、`ProjectSettings/` 配下へ直接シリアライズする。

### 方針

- 保存先は `ProjectSettings/<Name>.json`。**素のテキストを `File.WriteAllText` /
  `File.ReadAllText` で直接読み書き**し、`JsonUtility` でシリアライズする。
  - アセットDB に現れないため Project ビューや Inspector を汚さない。
  - VCS にコミットすればチーム単位で共有できる（個人マシン単位なら `EditorPrefs`）。
  - キャッシュは素の C# オブジェクトに持つ。ドメインリロードで `null` になり次回
    ファイルから再構築されるため、`UnityEngine.Object` の寿命問題に巻き込まれない。
- 導入先ではパッケージ本体（`Packages/.../`）は書き込み不可。同意状態のような
  プロジェクト固有の状態は **必ず導入先プロジェクトの `ProjectSettings/`** に書く。
- 実装例: [`AunCastConsentStore`](../Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Editor/AunCastConsentStore.cs)。
  メジャーバージョンを記録し、メジャー更新時のみ再同意を促す。

> **落とし穴（重要）**: エディタ状態の永続化に Unity のシリアライズドファイル API
> （`InternalEditorUtility.SaveToSerializedFileAndForget` や `ScriptableSingleton<T>`）を
> 使うと、ドメインリロード直後に型解決が失敗してロード結果が目的の型へキャストできず、
> **保存したはずの状態がたびたび既定値へ戻る**ことがある（特に `private` ネスト型の
> `ScriptableObject` は `m_Script` が `{fileID: 0}` になり `m_EditorClassIdentifier` でしか
> 紐づかず不安定）。この種の小さな状態は **`JsonUtility` + 素の File IO** で書くのが確実。
>
> なお同意状態を **コンポーネントのシリアライズフィールド** に持つ案もあるが、配布
> プレハブ本体に値が焼き込まれると導入先全員が「同意済み」になりゲートが無効化される
> リスクがあるため避けた。

### 同意ゲート（ソフト抑止）の作り方

- カスタムエディタ（`AunCastSettingsInspector`）の `OnInspectorGUI` 冒頭で
  `HasConsented` を判定し、未同意なら同意 UI のみ描画して **早期 return** する。
- 規約 PDF は GUID 経由（`LoadAssetByGuid` → `AssetDatabase.OpenAsset`）で開く。
  パス文字列リテラルは使わない。
- 同意確定後に描画する UI 構成が変わるため、`GUIUtility.ExitGUI()` で当該フレームの
  GUI をやり直す。
- これは導線上の抑止であり実行時の動作は止めない。アップロード自体をブロックするには
  VRCSDK の `IVRCSDKBuildRequestedCallback` で中断する必要がある（本実装は未対応）。

## 11. インスペクタのローカライズ規約

カスタムエディタの **UI 表示文字列は日英両対応** とする。コメント・`Debug.Log`・
`Undo` 名は対象外で、コメントは日本語のまま維持する（プロジェクト方針）。

### 文字列の切り替え

- 言語判定は [`AunCastEditorLocalization.Localize(ja, en)`](../Packages/tokyo.chigiri.pasocommate.auncast/Scripts/Editor/AunCastEditorLocalization.cs)
  に集約する。表示言語は `EditorLanguage`（`Auto` / `Japanese` / `English`）で、
  `Auto` のみ `Application.systemLanguage` に追従する。
  - 言語設定は個人のエディタ設定として **EditorPrefs**（マシン単位）に保存し、
    バナー内の言語セレクタ（`AunCastInspectorBanner`）から手動切替できる。
    切替時は `InternalEditorUtility.RepaintAllViews()` で全インスペクタへ即時反映する。
- `LabelField` / `HelpBox` / `GUILayout.Button` / `DisplayDialog` 等の直接呼び出しは
  引数を `Localize(ja, en)` で包む。
- ラベル付きフィールドは `AunCastSettingsInspector` のヘルパーに合わせる。
  `L(ja, en, fieldName, tooltipJa, tooltipEn)` が `GUIContent` を返し、
  `SliderField` / `IntSliderField` / `ToggleField` / `TextField` も同じ
  `(ja, en, fieldName, tooltipJa, tooltipEn, …)` 形を取る。
  - **Alt 押下中は `fieldName`（backing フィールド名）を表示** する開発者補助を兼ねる。
    フィールド名は英訳ではなく C# の変数名を渡す。
- 補間を含む文字列も `Localize($"…{x}…", $"…{x}…")` の形で両言語化できる。

### 対象外（日本語のまま）

- ソースコメント、`Debug.Log` / `Debug.LogWarning`（コンソール出力）、
  `Undo.RecordObject` などの操作名。

## 12. ContentScaler「設計キャンバス」パターンとサイズ追従

パネル（`PortablePanel` / `WallControlPanel`）配下の `ContentScaler` は、
**固定の設計解像度（PortablePanel は 900×640）を持つ単一キャンバス**であり、
`localScale`（例: 0.1）でパネルのローカル単位へ縮小する役割を持つ。

- `ContentScaler` の **Anchor は中央一点 (0.5,0.5) で固定**する。親へストレッチ追従
  させると rect サイズが親に引っ張られ、さらに `localScale` が掛かってレイアウトが
  破綻する。サイズの主従が「ContentScaler = マスター」であることを Anchor で表現している。
- `ContentScaler` 直下の `Background` / `*ContentArea` はストレッチ (0,0)-(1,1) で
  ContentScaler に完全追従する。よって**コンテンツの正サイズ＝`ContentScaler.sizeDelta`**。
- パネル本体・判定コライダーは ContentScaler から導出する。不変条件:

  ```
  PortablePanel.sizeDelta = ContentScaler.sizeDelta × ContentScaler.localScale
  BoxCollider.size        = (PortablePanel.sizeDelta.xy, z 据え置き)
  ```

- 設計サイズは `AunCastTheme.portableContentSize` に保持し、`AunCastThemeApplier`
  （エディタ時セットアップ）が上記の追従を一括適用する。書き換えた RectTransform /
  BoxCollider は `RecordPrefabInstancePropertyModifications` でプレハブオーバーライド
  記録する。
- 注意: ContentScaler 配下の**個別 UI（ボタン等）は絶対配置**のものが多く、設計サイズを
  変えても自動リフローしない。アスペクト比を大きく変える場合は内部レイアウトの再調整が要る。
- 物理的な見かけサイズだけ変えたい場合は `localScale` / `menuScale`（`UserStatusPanel`）
  側で行い、`sizeDelta` は触らない。
