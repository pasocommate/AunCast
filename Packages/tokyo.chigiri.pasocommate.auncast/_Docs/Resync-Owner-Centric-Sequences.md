# Resync Owner-Centric Sync Model: Sequence Diagrams

SDK 3.10.2 の `SendCustomNetworkEvent` パラメータ対応 + `[NetworkCallable]` を活用し、
クライアントの ownership 取得を全廃して Owner 一元管理に移行する新設計のシーケンス図。

## 1. 個人 Resync（障害検知 / 手動）

```mermaid
sequenceDiagram
    participant C as Client (LDPC)
    participant R as ResyncCoordinator
    participant O as Owner (ResyncCoordinator)

    Note over C: Active 異常検知 or ボタン押下

    C->>O: SendCustomNetworkEvent(Owner, "OnResyncRequest", slotIndex)
    Note over C: _localState = REQUEST_PENDING<br/>ポーリング開始

    O->>O: resyncState[slot] = QUEUED<br/>userTimestamp[slot] = serverTime<br/>MarkDirty()
    O-->>R: RequestSerialization()

    Note over O: TickScheduler (1秒周期)
    O->>O: 空き枠あり → resyncState[slot] = GRANTED<br/>RequestSerialization()

    R-->>C: OnDeserialization (resyncState=GRANTED を観測)
    Note over C: _localState = RESERVED → StartStandbyConnect

    C->>O: SendCustomNetworkEvent(Owner, "OnReportRunning", slotIndex)
    O->>O: resyncState[slot] = RUNNING<br/>MarkDirty()

    Note over C: Standby LoadURL → Ready → Play → Verify → Crossfade

    C->>O: SendCustomNetworkEvent(Owner, "OnReportSuccess", slotIndex)
    O->>O: resyncState[slot] = NONE<br/>MarkDirty()
    Note over C: _localState = COOLDOWN (5秒)
```

## 2. 個人 Resync: 失敗時

```mermaid
sequenceDiagram
    participant C as Client (LDPC)
    participant O as Owner (ResyncCoordinator)

    Note over C: Standby 接続タイムアウト

    C->>O: SendCustomNetworkEvent(Owner, "OnReportFail", slotIndex)
    O->>O: resyncState[slot] = NONE<br/>MarkDirty()

    alt Active がまだ生存
        Note over C: _localState = COOLDOWN (5秒)
    else 両系統失敗
        Note over C: _localState = RETRY_WAIT<br/>exponential backoff
    end
```

## 3. Global Resync（スタッフ操作）

グローバル Resync は個別 Resync と同じフローを共用する。専用の状態コードやイベントは設けない。

```mermaid
sequenceDiagram
    participant S as Staff
    participant O as Owner (ResyncCoordinator)
    participant R as ResyncCoordinator (synced)
    participant C1 as Client A
    participant C2 as Client B

    S->>O: TriggerGlobalResync()<br/>(ownership 取得 → 直接書換)
    Note over O: 全アクティブスロット:<br/>STATE_NONE → STATE_QUEUED<br/>同一 serverTime で timestamp 設定<br/>CompressTimestamps()<br/>RequestSerialization()

    R-->>C1: OnDeserialization
    R-->>C2: OnDeserialization

    Note over C1: PollResyncCoordinator()<br/>ACTIVE_PLAYING 中に coordState == QUEUED を検知<br/>→ adoption（_requestReason = MANUAL）<br/>_localState = REQUEST_PENDING

    Note over O: TickScheduler (1秒周期)<br/>空き枠分だけ QUEUED → GRANTED<br/>RequestSerialization()

    R-->>C1: OnDeserialization (GRANTED を観測)
    Note over C1: PollResyncCoordinator()<br/>REQUEST_PENDING 中に GRANTED 検知<br/>_localState = RESERVED<br/>→ StartStandbyConnect

    C1->>O: SendCustomNetworkEvent(Owner, "OnReportRunning", slotIndex)
    O->>O: resyncState[slot] = RUNNING<br/>MarkDirty()

    Note over C1: Standby LoadURL → Ready → Verify → Crossfade

    C1->>O: SendCustomNetworkEvent(Owner, "OnReportSuccess", slotIndex)
    O->>O: resyncState[slot] = NONE<br/>MarkDirty()
    Note over C1: _localState = COOLDOWN (5秒)

    Note over O: 枠が空いたので次の TickScheduler で<br/>Client B が QUEUED → GRANTED
    Note over C2: 同様のフローで Resync 実行
```

## 4. キャンセルと adoption 抑制

```mermaid
sequenceDiagram
    participant C as Client (LDPC)
    participant O as Owner (ResyncCoordinator)
    participant R as ResyncCoordinator (synced)

    Note over C: REQUEST_PENDING or RESERVED 中にユーザーがキャンセル

    C->>O: SendCustomNetworkEvent(Owner, "OnCancelSlot", slotIndex)
    Note over C: _adoptionSuppressedUntil = now + 2秒<br/>_localState = ACTIVE_PLAYING

    O->>O: resyncState[slot] = NONE<br/>MarkDirty()
    R-->>C: OnDeserialization (STATE_NONE を観測)
    Note over C: _adoptionSuppressedUntil = 0 (抑制解除)

    Note right of C: --- 抑制中に Global Resync が来た場合 ---

    Note over O: TriggerGlobalResync() で QUEUED 再設定
    R-->>C: OnDeserialization (QUEUED を観測)
    Note over C: PollResyncCoordinator()<br/>now < _adoptionSuppressedUntil<br/>→ 採用抑制（-1 を返す）
```

## 5. スロット割当（Join + フォールバック）

```mermaid
sequenceDiagram
    participant C as Client (LDPC)
    participant O as Owner (ResyncCoordinator)
    participant R as ResyncCoordinator (synced)

    Note over O: OnPlayerJoined(player)<br/>空きスロット割当<br/>RequestSerialization()

    R-->>C: OnDeserialization
    Note over C: EnsureSlotAssigned()<br/>FindSlotByPlayerId(localId)<br/>→ スロット発見

    Note right of C: --- フォールバック ---<br/>Owner が割当を逃した場合

    loop 毎フレーム (5秒間隔で再送)
        C->>C: FindSlotByPlayerId(localId) → -1
        C->>O: SendCustomNetworkEvent(Owner, "OnRequestSlot", playerId)
        O->>O: VRCPlayerApi.GetPlayerById(id) で存在確認<br/>空きスロット割当<br/>MarkDirty()
        R-->>C: OnDeserialization → スロット発見
    end
```

## 6. イベント再送（デリバリー保証なし対策）

```mermaid
sequenceDiagram
    participant C as Client (LDPC)
    participant O as Owner (ResyncCoordinator)
    participant R as ResyncCoordinator (synced)

    C->>O: SendCustomNetworkEvent(Owner, "OnResyncRequest", slot)
    Note over C: _localState = REQUEST_PENDING<br/>_requestStartedAt = now

    Note over O: イベントロスト（ネットワーク障害）

    loop ポーリング (毎フレーム)
        C->>R: GetResyncState(slot) → STATE_NONE のまま
    end

    Note over C: 3秒経過 → 再送
    C->>O: SendCustomNetworkEvent(Owner, "OnResyncRequest", slot)
    O->>O: resyncState[slot] = QUEUED<br/>MarkDirty()
    R-->>C: OnDeserialization → STATE_QUEUED 確認

    Note over C: REQUEST_PENDING にタイムアウトなし<br/>（手動キャンセルで対応）
```

## 旧設計との主な違い

| 観点 | 旧設計 (ownership ベース) | 新設計 (Owner-Centric) |
|---|---|---|
| クライアント操作 | `TryTakeOwnership()` → 同期変数書換 → `RequestSerialization()` | `SendCustomNetworkEvent(Owner, ...)` 1行 |
| 書き込み権限 | 全クライアントが ownership を奪って書き換え可能 | Owner のみ（スタッフ操作を除く） |
| 競合リスク | 同時 ownership 取得で先行変更が消失（last-writer-wins） | なし（Owner が単一書き込み者） |
| 同期パケット | 1,008 bytes | 420 bytes (-58%) |
| Global Resync 結果報告 | パラメータなし SendCustomNetworkEvent (slotIndex 不明) | 通常の OnReportSuccess / OnReportFail を共用（個別 Resync と同一フロー） |
| デリバリー保証 | ownership + RequestSerialization (確実) | fire-and-forget + ポーリング確認 + タイムアウト清掃 |
| スタッフ操作 | ownership ベース | ownership ベース（変更なし） |
