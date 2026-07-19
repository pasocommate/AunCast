
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// ワールド全体の Resync 予約・Grant 制御を行う同期オブジェクト（Design Section 9.2-C, 12, 13）。
    /// Owner が全スロットを一元管理し、クライアントは SendCustomNetworkEvent で通知する。
    /// 配信サーバへの同時接続数を制御することで、大人数インスタンスでのサーバ過負荷を防ぐ。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AunCastResyncCoordinator : UdonSharpBehaviour
    {
        // --- 状態コード (Design Section 13.3) ---
        // Resync スロットの状態遷移: NONE → QUEUED → GRANTED → RUNNING → NONE
        public const int STATE_NONE = 0;
        public const int STATE_QUEUED = 1;
        public const int STATE_GRANTED = 2;
        public const int STATE_RUNNING = 3;

        // --- ドリフト Resync 閾値インデックス ---
        // float の Infinity は同期せず、OFF を含む固定段階のインデックスを同期する。
        public const int DRIFT_THRESHOLD_50_MS = 0;
        public const int DRIFT_THRESHOLD_100_MS = 1;
        public const int DRIFT_THRESHOLD_150_MS = 2;
        public const int DRIFT_THRESHOLD_200_MS = 3;
        public const int DRIFT_THRESHOLD_250_MS = 4;
        public const int DRIFT_THRESHOLD_300_MS = 5;
        public const int DRIFT_THRESHOLD_400_MS = 6;
        public const int DRIFT_THRESHOLD_500_MS = 7;
        public const int DRIFT_THRESHOLD_700_MS = 8;
        public const int DRIFT_THRESHOLD_1_SEC = 9;
        public const int DRIFT_THRESHOLD_2_SEC = 10;
        public const int DRIFT_THRESHOLD_3_SEC = 11;
        public const int DRIFT_THRESHOLD_5_SEC = 12;
        public const int DRIFT_THRESHOLD_OFF = 13;

        // --- Inspector パラメータ (Design Section 20) ---
        [Header("Coordinator Settings")]
        [Tooltip("Grant 後の接続開始タイムアウト（秒）。ネットワーク同期遅延を考慮して長めに設定")]
        [SerializeField] private float grantTimeoutSec = 10.0f;

        [Tooltip("Running 状態の最大継続時間（秒）。クライアント側サイクルタイムアウトより長く設定")]
        [SerializeField] private float runningTimeoutSec = 50.0f;

        [Tooltip("デバッグログを有効にする")]
        [SerializeField] private bool debugLoggingEnabled = false;

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        [Header("References")]
        /// <summary>各クライアントの再生状態を集約するモニタ。接続数上限の判断に使う。</summary>
        [SerializeField] private AunCastPlaybackMonitor playbackMonitor;
        [Tooltip("同期変数の更新を通知する先（AunCastStaffControlPanel を配線）。UI 具象型に依存しないため UdonSharpBehaviour で受ける。")]
        [SerializeField] private UdonSharpBehaviour staffNotifyTarget;
        [Tooltip("Global Force Reboot の同期受信を即時通知するローカル Controller。")]
        [SerializeField] private AunCastDualPlayerController forceRebootNotifyTarget;

        // --- 同期変数: スロット管理 (Design Section 13.2) ---
        /// <summary>各スロットに割り当てられたプレイヤーの ID。0 = 空きスロット。</summary>
        [UdonSynced] private short[] userPlayerId;
        /// <summary>各スロットの Resync 状態コード (STATE_*)。</summary>
        [UdonSynced] private byte[] resyncState;
        // タイムスタンプ圧縮: offset = 最大値, delta = (offset - actual) の 0.1 秒単位
        // 全タイムスタンプを最新値からの相対差として ushort に圧縮し、同期帯域を節約する。
        [UdonSynced] private float userTimestampOffset;
        [UdonSynced] private ushort[] userTimestampDelta;

        /// <summary>グローバル強制リブートのシーケンス番号。インクリメントで全クライアントにリブートを通知する。</summary>
        [UdonSynced] private short globalForceRebootSeq;

        // --- 同期変数: ランタイム変更可能パラメータ ---
        [UdonSynced]
        [Tooltip("同時Resync上限（スタッフが変更可能）")]
        [SerializeField] private byte maxConcurrentResyncUsers = 10;

        [UdonSynced]
        [Tooltip("配信サーバへの同時接続上限（スタッフが変更可能）")]
        [SerializeField] private byte maxConnectionLimit = DEFAULT_CONNECTION_LIMIT;

        [UdonSynced]
        [Tooltip("ドリフトによる自動 Resync の閾値インデックス（スタッフが変更可能）")]
        [SerializeField] private byte driftResyncThresholdIndex = DRIFT_THRESHOLD_100_MS;

        // --- Owner ローカル ---
        /// <summary>Owner だけが保持する高精度タイムスタンプ。同期用に圧縮して送る。</summary>
        private float[] _ownerTimestamp;
        private float _tickTimer;
        private const float TICK_INTERVAL = 1.0f;
        // 同期スロット配列の固定長上限。定義元は本クラスで、AunCastPlaybackMonitor は
        // この値を参照してビットパック配列長を Coordinator の配列長に一致させる。
        public const int MAX_PLAYERS = 82;
        private const int DEFAULT_CONNECTION_LIMIT = 100;
        private const int MIN_CONNECTION_LIMIT = 1;
        private const int MAX_CONNECTION_LIMIT = 255;
        /// <summary>待ち時間推定に使う平均 Resync 所要時間（経験値）。</summary>
        private const float AVG_RESYNC_DURATION_SEC = 8f;
        /// <summary>同一フレーム内の複数変更をバッチして 1 回の serialize にまとめるためのフラグ。</summary>
        private bool _serializationPending;
        /// <summary>非 Owner が bunch を一度でも適用済みか。IsNetworkSettled だけでは適用完了を保証しない（Quest 実測）。</summary>
        private bool _syncApplied;
        /// <summary>初回同期値を履歴ではなく基準値として扱うための Force Reboot シーケンス保持。</summary>
        private bool _hasObservedForceRebootSeq;
        private short _lastObservedForceRebootSeq;

        // =====================================================================
        //  Unity ライフサイクル
        // =====================================================================

        private void Start()
        {
            if (userPlayerId == null
                || userPlayerId.Length != MAX_PLAYERS)
                InitializeArrays();

            if (_ownerTimestamp == null)
                _ownerTimestamp = new float[MAX_PLAYERS];

            if (maxConnectionLimit < MIN_CONNECTION_LIMIT)
                maxConnectionLimit = DEFAULT_CONNECTION_LIMIT;
            if (driftResyncThresholdIndex > DRIFT_THRESHOLD_OFF)
                driftResyncThresholdIndex = DRIFT_THRESHOLD_100_MS;
        }

        private void Update()
        {
            if (!CanMutateState()) return;

            // 遅延シリアライズ: 同一フレーム内の複数変更を 1 回のネットワーク送信にまとめる
            if (_serializationPending)
            {
                CompressTimestamps();
                RequestSerialization();
                NotifyObservers();
                _serializationPending = false;
            }

            _tickTimer += Time.deltaTime;
            if (_tickTimer < TICK_INTERVAL) return;
            _tickTimer = 0f;

            TickScheduler();
        }

        public override void OnDeserialization()
        {
            _syncApplied = true;
            RestoreOwnerTimestamps();
            NotifyObservers();

            // 初回 bunch の値は Join 前に発行された履歴として扱い、Reboot を発火させない。
            // 2回目以降の差分だけを Controller へ通知し、イベント欠落時は Controller の
            // TickSlow ポーリングが保険として検出する。
            if (_hasObservedForceRebootSeq
                && _lastObservedForceRebootSeq != globalForceRebootSeq
                && forceRebootNotifyTarget != null)
            {
                forceRebootNotifyTarget.OnCoordinatorForceRebootChanged();
            }
            _lastObservedForceRebootSeq = globalForceRebootSeq;
            _hasObservedForceRebootSeq = true;
        }

        /// <summary>監視対象の UI パネルに再描画を促す。</summary>
        private void NotifyObservers()
        {
            if (staffNotifyTarget != null) staffNotifyTarget.SendCustomEvent("OnCoordinatorChanged");
        }

        // =====================================================================
        //  配列初期化
        // =====================================================================

        private void InitializeArrays()
        {
            userPlayerId = new short[MAX_PLAYERS];
            resyncState = new byte[MAX_PLAYERS];
            userTimestampDelta = new ushort[MAX_PLAYERS];
        }

        // =====================================================================
        //  遅延シリアライズ
        // =====================================================================

        /// <summary>変更をマークし、次フレームの Update でまとめてシリアライズする。</summary>
        private void MarkDirty()
        {
            _serializationPending = true;
        }

        /// <summary>Owner のみ保持する float タイムスタンプを、同期帯域節約のため ushort 差分に圧縮する。</summary>
        private void CompressTimestamps()
        {
            float max = float.MinValue;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (resyncState[i] == STATE_NONE) continue;
                if (_ownerTimestamp[i] > max) max = _ownerTimestamp[i];
            }
            userTimestampOffset = (max == float.MinValue) ? 0f : max;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (resyncState[i] == STATE_NONE) { userTimestampDelta[i] = 0; continue; }
                float delta = (userTimestampOffset - _ownerTimestamp[i]) * 10f;
                userTimestampDelta[i] = (ushort)Mathf.Min(Mathf.Round(delta), 65535f);
            }
        }

        // =====================================================================
        //  スケジューラ (Design Section 21.2)
        // =====================================================================

        /// <summary>
        /// 毎秒 Owner が実行するスケジューラ。
        /// タイムアウト掃除 → 空き枠計算 → キュー先頭から Grant を発行する。
        /// </summary>
        private void TickScheduler()
        {
            float serverTime = GetServerTime();
            bool changed = CleanupExpiredStates(serverTime);

            // 同時 Resync 枠: maxConcurrentResyncUsers から既に走っている数を引いた残り
            int available = maxConcurrentResyncUsers - CountGrantedOrRunning();

            // 同時接続上限による制約
            // Resync 中のユーザーは Active 側 + Standby 側で 2 接続を消費するため、
            // AunCastPlaybackMonitor のカウント（1 スロット 1 ビット）に加え
            // GRANTED/RUNNING 分のスタンバイ接続を上乗せする
            if (playbackMonitor != null)
            {
                int totalConnections = playbackMonitor.GetPlayingEstimateCount()
                                     + playbackMonitor.GetConnectingEstimateCount()
                                     + CountGrantedOrRunning();
                int connectionHeadroom = maxConnectionLimit - totalConnections;
                if (connectionHeadroom < available)
                    available = connectionHeadroom;
            }

            // FIFO で最古のキュー待ちから順に Grant を付与
            while (available > 0)
            {
                int bestSlot = SelectNextQueuedUser();
                if (bestSlot < 0) break;

                resyncState[bestSlot] = STATE_GRANTED;
                _ownerTimestamp[bestSlot] = serverTime;
                changed = true;
                available--;

                if (debugLoggingEnabled)
                    LogMessage($"Granted slot {bestSlot} (player {userPlayerId[bestSlot]})");
            }

            if (changed) MarkDirty();
        }

        /// <summary>GRANTED/RUNNING のまま応答がないスロットをタイムアウトで回収する。</summary>
        private bool CleanupExpiredStates(float serverTime)
        {
            bool changed = false;

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                int state = resyncState[i];
                if (state != STATE_GRANTED && state != STATE_RUNNING) continue;

                float elapsed = serverTime - _ownerTimestamp[i];
                if (elapsed < 0f) continue;

                float timeout = state == STATE_GRANTED ? grantTimeoutSec : runningTimeoutSec;
                if (elapsed > timeout)
                {
                    resyncState[i] = STATE_NONE;
                    changed = true;
                    if (debugLoggingEnabled)
                        LogMessage(state == STATE_GRANTED
                            ? $"Grant expired for slot {i}"
                            : $"Running timeout for slot {i}");
                }
            }

            return changed;
        }

        /// <summary>QUEUED の中で最もタイムスタンプが古い（最も長く待っている）スロットを返す。</summary>
        private int SelectNextQueuedUser()
        {
            int bestSlot = -1;
            float bestTime = float.MaxValue;

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (resyncState[i] == STATE_QUEUED && _ownerTimestamp[i] < bestTime)
                {
                    bestTime = _ownerTimestamp[i];
                    bestSlot = i;
                }
            }

            return bestSlot;
        }

        private int CountGrantedOrRunning()
        {
            int count = 0;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (resyncState[i] == STATE_GRANTED || resyncState[i] == STATE_RUNNING)
                    count++;
            }
            return count;
        }

        // =====================================================================
        //  [NetworkCallable] ハンドラ — クライアントから Owner への通知
        // =====================================================================

        /// <summary>クライアントが Resync を予約する。Owner 側でキューに追加する。</summary>
        [NetworkCallable]
        public void OnResyncRequest(int slotIndex)
        {
            if (!CanMutateState()) return;
            if (!ValidateSlotIndex(slotIndex)) return;
            if (resyncState[slotIndex] != STATE_NONE) return;

            resyncState[slotIndex] = STATE_QUEUED;
            _ownerTimestamp[slotIndex] = GetServerTime();
            MarkDirty();
            TickScheduler();

            if (debugLoggingEnabled)
                LogMessage($"Slot {slotIndex} queued for Resync");
        }

        /// <summary>Standby 接続が実際に開始されたことをクライアントが報告する。</summary>
        [NetworkCallable]
        public void OnReportRunning(int slotIndex)
        {
            if (!CanMutateState()) return;
            if (!ValidateSlotIndex(slotIndex)) return;
            if (resyncState[slotIndex] != STATE_GRANTED) return;

            resyncState[slotIndex] = STATE_RUNNING;
            _ownerTimestamp[slotIndex] = GetServerTime();
            MarkDirty();
            TickScheduler();
        }

        /// <summary>Resync 成功をクライアントが報告する。スロットを解放して次の Grant に回す。</summary>
        [NetworkCallable]
        public void OnReportSuccess(int slotIndex)
        {
            if (!CanMutateState()) return;
            if (!ValidateSlotIndex(slotIndex)) return;

            int state = resyncState[slotIndex];
            if (state != STATE_RUNNING && state != STATE_GRANTED) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
            LogMessage($"Report success cleared slot {slotIndex} from state {state}");
        }

        /// <summary>Resync 失敗をクライアントが報告する。スロットを解放する。</summary>
        [NetworkCallable]
        public void OnReportFail(int slotIndex)
        {
            if (!CanMutateState()) return;
            if (!ValidateSlotIndex(slotIndex)) return;

            int state = resyncState[slotIndex];
            if (state != STATE_RUNNING && state != STATE_GRANTED) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
            LogMessage($"Report fail cleared slot {slotIndex} from state {state}");
        }

        /// <summary>クライアントが Resync をキャンセルする。</summary>
        [NetworkCallable]
        public void OnCancelSlot(int slotIndex)
        {
            if (!CanMutateState()) return;
            if (!ValidateSlotIndex(slotIndex)) return;
            if (resyncState[slotIndex] == STATE_NONE) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
            LogMessage($"Cancel cleared slot {slotIndex}");
        }

        // =====================================================================
        //  スロット管理 (Design Section 13.1)
        //  割当はクライアント起点の Pull 型に一本化する。クライアントは settle 後に
        //  OnRequestSlot を送り、未達なら 5 秒間隔で再送するため、Owner 側の
        //  OnPlayerJoined での Push 割当（settle 順序に依存する）は持たない。
        // =====================================================================

        /// <summary>スロット未割当のクライアントが空きスロットを要求する（唯一の割当経路）。</summary>
        [NetworkCallable]
        public void OnRequestSlot(int playerId)
        {
            if (!CanMutateState()) return;
            if (userPlayerId == null) return;
            if (VRCPlayerApi.GetPlayerById(playerId) == null) return;

            short pid = (short)playerId;

            // 既に割当済みならスキップ
            for (int i = 0; i < MAX_PLAYERS; i++)
                if (userPlayerId[i] == pid) return;

            // 既にインスタンスにいないプレイヤーの残留スロットをクリーンアップ
            // （OnPlayerLeft のシリアライズがロストした場合のフォールバック）
            bool cleaned = false;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (userPlayerId[i] == 0) continue;
                VRCPlayerApi existing = VRCPlayerApi.GetPlayerById(userPlayerId[i]);
                if (existing == null || !existing.IsValid())
                {
                    if (debugLoggingEnabled)
                        LogMessage($"Stale slot {i} (player {userPlayerId[i]}) cleaned up");
                    ResetSlot(i);
                    cleaned = true;
                }
            }

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (userPlayerId[i] == 0)
                {
                    InitializeSlot(i, pid);
                    MarkDirty();
                    if (debugLoggingEnabled)
                        LogMessage($"Slot assigned: player {playerId} → slot {i}");
                    return;
                }
            }

            if (cleaned) MarkDirty();
            LogWarning($"OnRequestSlot: No empty slot for player {playerId}");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!CanMutateState()) return;
            if (userPlayerId == null) return;

            short playerId = (short)player.playerId;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (userPlayerId[i] == playerId)
                {
                    ResetSlot(i);

                    // Rejoin 等でワールド破棄直前に呼ばれると遅延シリアライズが
                    // ロストするため即時送信する。AunCastPlaybackMonitor は自身の OnPlayerLeft で掃除する。
                    CompressTimestamps();
                    RequestSerialization();
                    NotifyObservers();
                    _serializationPending = false;

                    if (debugLoggingEnabled)
                        LogMessage($"Player {playerId} left, slot {i} freed");
                    return;
                }
            }
        }

        // =====================================================================
        //  グローバル Resync (Design Section 12.5) — スタッフ操作は ownership ベース
        // =====================================================================

        /// <summary>全アクティブユーザーを一斉にキューに入れる。配信サーバ側の再起動後等に使う。</summary>
        public void TriggerGlobalResync()
        {
            if (!TryTakeOwnership()) return;

            if (_ownerTimestamp == null)
                _ownerTimestamp = new float[MAX_PLAYERS];

            float serverTime = GetServerTime();
            int total = 0;

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (userPlayerId[i] == 0) continue;
                if (resyncState[i] != STATE_NONE) continue;

                resyncState[i] = STATE_QUEUED;
                _ownerTimestamp[i] = serverTime;
                total++;
            }

            CompressTimestamps();
            RequestSerialization();
            NotifyObservers();
            TL($"a=GLOBAL_RESYNC total={total}");
            LogMessage($"Global Resync triggered: {total} users queued");
        }

        /// <summary>全クライアントに Active 直接リブートを命じる。Resync では解消しない重度障害時の最終手段。</summary>
        public void TriggerGlobalForceReboot()
        {
            if (!TryTakeOwnership()) return;
            globalForceRebootSeq++;
            RequestSerialization();
            NotifyObservers();
            if (forceRebootNotifyTarget != null)
                forceRebootNotifyTarget.OnCoordinatorForceRebootChanged();
            TL($"a=GLOBAL_FORCE_REBOOT_TRIGGER seq={globalForceRebootSeq}");
            LogMessage($"Global force reboot triggered: seq={globalForceRebootSeq}");
        }

        // =====================================================================
        //  Getters（UI パネル・Controller から参照）
        // =====================================================================

        /// <summary>指定スロットの Resync 状態を返す。クライアント側が Grant 検知に使う。</summary>
        public int GetResyncState(int slotIndex)
        {
            if (resyncState == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return STATE_NONE;
            return resyncState[slotIndex];
        }

        /// <summary>指定スロットに割り当てられたプレイヤー ID を返す。</summary>
        public int GetUserPlayerId(int slotIndex)
        {
            if (userPlayerId == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0;
            return userPlayerId[slotIndex];
        }

        /// <summary>圧縮タイムスタンプを復元して返す。待ち時間推定や UI 表示に使う。</summary>
        public float GetUserTimestamp(int slotIndex)
        {
            if (userTimestampDelta == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0f;
            return userTimestampOffset - userTimestampDelta[slotIndex] * 0.1f;
        }

        public int GetMaxPlayers() { return MAX_PLAYERS; }
        public AunCastPlaybackMonitor GetPlaybackMonitor() { return playbackMonitor; }
        public int GetMaxConcurrentResyncUsers() { return maxConcurrentResyncUsers; }
        public int GetMaxConnectionLimit() { return maxConnectionLimit; }
        public int GetMinConnectionLimit() { return MIN_CONNECTION_LIMIT; }
        public int GetMaxConnectionLimitCap() { return MAX_CONNECTION_LIMIT; }
        public int GetDriftResyncThresholdIndex() { return driftResyncThresholdIndex; }
        public bool IsDriftResyncEnabled() { return driftResyncThresholdIndex != DRIFT_THRESHOLD_OFF; }
        /// <summary>現在のドリフト Resync 閾値を秒で返す。OFF の場合はメーター表示用に 5 秒を返す。</summary>
        public float GetDriftResyncThresholdSec()
        {
            switch (driftResyncThresholdIndex)
            {
                case DRIFT_THRESHOLD_50_MS: return 0.05f;
                case DRIFT_THRESHOLD_100_MS: return 0.1f;
                case DRIFT_THRESHOLD_150_MS: return 0.15f;
                case DRIFT_THRESHOLD_200_MS: return 0.2f;
                case DRIFT_THRESHOLD_250_MS: return 0.25f;
                case DRIFT_THRESHOLD_300_MS: return 0.3f;
                case DRIFT_THRESHOLD_400_MS: return 0.4f;
                case DRIFT_THRESHOLD_500_MS: return 0.5f;
                case DRIFT_THRESHOLD_700_MS: return 0.7f;
                case DRIFT_THRESHOLD_1_SEC: return 1f;
                case DRIFT_THRESHOLD_2_SEC: return 2f;
                case DRIFT_THRESHOLD_3_SEC: return 3f;
                case DRIFT_THRESHOLD_5_SEC: return 5f;
                default: return 5f;
            }
        }

        public string GetDriftResyncThresholdDisplayText()
        {
            switch (driftResyncThresholdIndex)
            {
                case DRIFT_THRESHOLD_50_MS: return "50 ms";
                case DRIFT_THRESHOLD_100_MS: return "100 ms";
                case DRIFT_THRESHOLD_150_MS: return "150 ms";
                case DRIFT_THRESHOLD_200_MS: return "200 ms";
                case DRIFT_THRESHOLD_250_MS: return "250 ms";
                case DRIFT_THRESHOLD_300_MS: return "300 ms";
                case DRIFT_THRESHOLD_400_MS: return "400 ms";
                case DRIFT_THRESHOLD_500_MS: return "500 ms";
                case DRIFT_THRESHOLD_700_MS: return "700 ms";
                case DRIFT_THRESHOLD_1_SEC: return "1 s";
                case DRIFT_THRESHOLD_2_SEC: return "2 s";
                case DRIFT_THRESHOLD_3_SEC: return "3 s";
                case DRIFT_THRESHOLD_5_SEC: return "5 s";
                default: return "OFF";
            }
        }
        public int GetGlobalForceRebootSeq() { return globalForceRebootSeq; }
        /// <summary>
        /// Join 時の同期データが適用済みで、同期状態の読み書きを開始できるか。
        /// IsNetworkSettled が真でも bunch 適用が遅れる実測があるため（Quest）、
        /// 非 Owner は初回 OnDeserialization の適用も要求する（Owner = 配信元は不要）。
        /// </summary>
        public bool IsInitialStateReady()
        {
            return Networking.IsNetworkSettled
                && (_syncApplied || Networking.IsOwner(gameObject));
        }

        public int GetQueuedCount()
        {
            if (resyncState == null) return 0;
            int count = 0;
            for (int i = 0; i < MAX_PLAYERS; i++)
                if (resyncState[i] == STATE_QUEUED) count++;
            return count;
        }

        public int GetPlayingEstimateCount()
        {
            return playbackMonitor != null ? playbackMonitor.GetPlayingEstimateCount() : 0;
        }

        public int GetConnectingEstimateCount()
        {
            return playbackMonitor != null ? playbackMonitor.GetConnectingEstimateCount() : 0;
        }

        /// <summary>指定スロットの推定待ち時間（秒）を返す。自分より前のキュー数と同時 Resync 数から概算する。</summary>
        public float EstimateWaitTime(int slotIndex)
        {
            if (resyncState == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0f;
            if (resyncState[slotIndex] != STATE_QUEUED) return 0f;

            float myTimestamp = GetUserTimestamp(slotIndex);
            int ahead = 0;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (i == slotIndex) continue;
                if (resyncState[i] == STATE_QUEUED && GetUserTimestamp(i) < myTimestamp) ahead++;
                if (resyncState[i] == STATE_GRANTED || resyncState[i] == STATE_RUNNING) ahead++;
            }

            int concurrent = maxConcurrentResyncUsers > 0 ? maxConcurrentResyncUsers : 1;
            return ((float)ahead / concurrent) * AVG_RESYNC_DURATION_SEC;
        }

        /// <summary>
        /// 待機列が解消して全 Resync が完了するまでの推定残り時間（秒）を返す。
        /// 待機者（QUEUED）がいない場合は 0 を返す。
        /// </summary>
        public float EstimateGlobalWaitTime()
        {
            if (resyncState == null) return 0f;
            int queued = 0;
            int active = 0;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                int s = resyncState[i];
                if (s == STATE_QUEUED) queued++;
                else if (s == STATE_GRANTED || s == STATE_RUNNING) active++;
            }
            if (queued == 0) return 0f;
            int concurrent = maxConcurrentResyncUsers > 0 ? maxConcurrentResyncUsers : 1;
            return Mathf.Ceil((float)(queued + active) / concurrent) * AVG_RESYNC_DURATION_SEC;
        }

        /// <summary>プレイヤー ID からスロットインデックスを逆引きする。未割当なら -1。</summary>
        public int FindSlotByPlayerId(int playerId)
        {
            if (userPlayerId == null) return -1;
            short pid = (short)playerId;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                if (userPlayerId[i] == pid) return i;
            }
            return -1;
        }

        /// <summary>スタッフが同時 Resync 上限をランタイムで変更する。Ownership を取得して同期する。</summary>
        public void SetMaxConcurrentResyncUsersRuntime(int value)
        {
            if (!TryTakeOwnership()) return;
            maxConcurrentResyncUsers = (byte)Mathf.Clamp(value, 1, 255);
            RequestSerialization();
            NotifyObservers();
        }

        /// <summary>スタッフが同時接続上限をランタイムで変更する。</summary>
        public void SetMaxConnectionLimitRuntime(int value)
        {
            if (!TryTakeOwnership()) return;
            maxConnectionLimit = (byte)Mathf.Clamp(value, MIN_CONNECTION_LIMIT, MAX_CONNECTION_LIMIT);
            RequestSerialization();
            NotifyObservers();
        }

        /// <summary>スタッフがドリフト Resync 閾値の固定段階を変更する。</summary>
        public void SetDriftResyncThresholdIndexRuntime(int value)
        {
            if (!TryTakeOwnership()) return;
            driftResyncThresholdIndex = (byte)Mathf.Clamp(value, DRIFT_THRESHOLD_50_MS, DRIFT_THRESHOLD_OFF);
            RequestSerialization();
            NotifyObservers();
        }

        // =====================================================================
        //  ユーティリティ
        // =====================================================================

        /// <summary>スロットにプレイヤーを割り当て、状態を初期化する。</summary>
        private void InitializeSlot(int i, short playerId)
        {
            userPlayerId[i] = playerId;
            resyncState[i] = STATE_NONE;
            if (_ownerTimestamp != null)
                _ownerTimestamp[i] = 0f;
            userTimestampDelta[i] = 0;
        }

        /// <summary>
        /// 自オブジェクトのスロット状態を初期化する。AunCastPlaybackMonitor 側のビット掃除は
        /// AunCastPlaybackMonitor 自身の OnPlayerLeft / OnPlayerJoined が担当する（ownership 分離のため）。
        /// </summary>
        private void ResetSlot(int i)
        {
            InitializeSlot(i, 0);
        }

        private bool ValidateSlotIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < MAX_PLAYERS
                && resyncState != null && resyncState.Length == MAX_PLAYERS;
        }

        private float GetServerTime()
        {
            return (float)Networking.GetServerTimeInSeconds();
        }

        /// <summary>Owner かつ Join 時同期が完了している場合だけ同期状態の変更を許可する。</summary>
        private bool CanMutateState()
        {
            return Networking.IsOwner(gameObject) && IsInitialStateReady();
        }

        /// <summary>同期された圧縮時刻から、Owner が Scheduler で使うローカル配列を復元する。</summary>
        private void RestoreOwnerTimestamps()
        {
            if (_ownerTimestamp == null || _ownerTimestamp.Length != MAX_PLAYERS)
                _ownerTimestamp = new float[MAX_PLAYERS];

            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                _ownerTimestamp[i] = resyncState != null
                    && i < resyncState.Length
                    && resyncState[i] != STATE_NONE
                    ? GetUserTimestamp(i)
                    : 0f;
            }
        }

        /// <summary>スタッフ操作用: Ownership を取得する。失敗時は操作を中断させる。</summary>
        private bool TryTakeOwnership()
        {
            // late-joiner が同期前の既定値を Owner として配信しないよう、操作自体を拒否する。
            if (!IsInitialStateReady()) return false;
            if (Networking.IsOwner(gameObject)) return true;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;

            Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return false;

            RestoreOwnerTimestamps();
            return true;
        }

        private void LogMessage(string message)
        {
            Debug.Log($"[AunCast/AunCastResyncCoordinator] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/AunCastResyncCoordinator] {message}", this);
        }

        /// <summary>タイムラインログをローカルのみ設定する。</summary>
        public void SetTimelineLoggingLocal(bool value)
        {
            _timelineLogging = value;
        }

        /// <summary>タイムラインログの有効状態を返す（UI 追従用。重要イベントはこの値によらず常時出力）。</summary>
        public bool GetTimelineLogging() { return _timelineLogging; }

        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={Networking.GetServerTimeInMilliseconds()} c=RC {eventAndData}");
        }
    }
}
