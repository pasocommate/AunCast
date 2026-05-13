# AunCast 実装パターン

プロジェクト内で繰り返し現れる、VRChat/Udon 上での同期変数・スタッフ権限操作・UI 双方向追従の設計ルールをまとめる。
VRChat/Udon API レベルの一般的な注意点は `VRChat-Udon-Development-Notes.md` を参照。

## 1. Manual sync + `AsStaff` メソッドパターン

`LocalDualPlayerController` は `BehaviourSyncMode.Manual`。スタッフ UI からの操作は
「ローカル直呼び」ではなく**専用の `SetXxxAsStaff` メソッド経由**で必ず同期する。

既存例: `PlayVideoAsStaff` / `StopVideoAsStaff`

> **注意**: 音量 (`_localVolume`) と無音自動 Resync (`_autoSilenceResyncEnabled`) は
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

- `StaffControlPanel` 側のイベントハンドラは **冒頭でアクセス権チェック**を入れる
  （`allowedUserNames` または `_passcodeUnlocked` のいずれかが満たされていること）。
  非スタッフが UI を操作した場合はアクセス拒否表示で UI を同期値へ戻す。
- 操作可能な UI は `interactable = isStaff` に設定し、
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
        OnXxxSliderChanged();   // IsStaff チェック付きハンドラ
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
    for (int i = 0; i < maxPlayers; i++)
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
