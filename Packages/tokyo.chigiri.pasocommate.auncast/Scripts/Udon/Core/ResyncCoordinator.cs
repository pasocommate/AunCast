
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
    public class ResyncCoordinator : UdonSharpBehaviour
    {
        // --- 状態コード (Design Section 13.3) ---
        // Resync スロットの状態遷移: NONE → QUEUED → GRANTED → RUNNING → NONE
        public const int STATE_NONE = 0;
        public const int STATE_QUEUED = 1;
        public const int STATE_GRANTED = 2;
        public const int STATE_RUNNING = 3;

        // --- Inspector パラメータ (Design Section 20) ---
        [Header("Coordinator Settings")]
        [Tooltip("最大プレイヤー数（VRChat Group+ 上限: 82）")]
        [SerializeField] private int maxPlayers = 82;

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
        [SerializeField] private PlaybackMonitor playbackMonitor;
        [Tooltip("同期変数の更新を通知する UI パネル（描画再更新用）")]
        [SerializeField] private StaffControlPanel staffPanel;

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
        [Tooltip("同時 Resync 実行数上限（スタッフが変更可能）")]
        [SerializeField] private byte maxConcurrentResyncUsers = 10;

        [UdonSynced]
        [Tooltip("配信サーバへの総接続数上限（スタッフが変更可能、0 = 無制限）")]
        [SerializeField] private byte maxConnectionLimit = 0;

        // --- Owner ローカル ---
        /// <summary>Owner だけが保持する高精度タイムスタンプ。同期用に圧縮して送る。</summary>
        private float[] _ownerTimestamp;
        private float _tickTimer;
        private const float TICK_INTERVAL = 1.0f;
        /// <summary>待ち時間推定に使う平均 Resync 所要時間（経験値）。</summary>
        private const float AVG_RESYNC_DURATION_SEC = 8f;
        /// <summary>同一フレーム内の複数変更をバッチして 1 回の serialize にまとめるためのフラグ。</summary>
        private bool _serializationPending;

        // =====================================================================
        //  Unity ライフサイクル
        // =====================================================================

        private void Start()
        {
            if (userPlayerId == null
                || userPlayerId.Length != maxPlayers)
                InitializeArrays();

            if (_ownerTimestamp == null)
                _ownerTimestamp = new float[maxPlayers];

            int minConn = maxPlayers + 1;
            int maxConn = maxPlayers * 2;
            if (maxConnectionLimit < minConn || maxConnectionLimit > maxConn)
                maxConnectionLimit = (byte)maxConn;
        }

        private void Update()
        {
            if (!Networking.IsOwner(gameObject)) return;

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
            NotifyObservers();
        }

        /// <summary>監視対象の UI パネルに再描画を促す。</summary>
        private void NotifyObservers()
        {
            if (staffPanel != null) staffPanel.OnCoordinatorChanged();
        }

        // =====================================================================
        //  配列初期化
        // =====================================================================

        private void InitializeArrays()
        {
            userPlayerId = new short[maxPlayers];
            resyncState = new byte[maxPlayers];
            userTimestampDelta = new ushort[maxPlayers];
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
            for (int i = 0; i < maxPlayers; i++)
            {
                if (resyncState[i] == STATE_NONE) continue;
                if (_ownerTimestamp[i] > max) max = _ownerTimestamp[i];
            }
            userTimestampOffset = (max == float.MinValue) ? 0f : max;
            for (int i = 0; i < maxPlayers; i++)
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

            // 総接続数上限による制約
            // Resync 中のユーザーは Active 側 + Standby 側で 2 接続を消費するため、
            // PlaybackMonitor のカウント（1 スロット 1 ビット）に加え
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

            for (int i = 0; i < maxPlayers; i++)
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

            for (int i = 0; i < maxPlayers; i++)
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
            for (int i = 0; i < maxPlayers; i++)
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
            if (!Networking.IsOwner(gameObject)) return;
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
            if (!Networking.IsOwner(gameObject)) return;
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
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlotIndex(slotIndex)) return;
            if (resyncState[slotIndex] != STATE_RUNNING) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
        }

        /// <summary>Resync 失敗をクライアントが報告する。スロットを解放する。</summary>
        [NetworkCallable]
        public void OnReportFail(int slotIndex)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlotIndex(slotIndex)) return;

            int state = resyncState[slotIndex];
            if (state != STATE_RUNNING && state != STATE_GRANTED) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
        }

        /// <summary>クライアントが Resync をキャンセルする。</summary>
        [NetworkCallable]
        public void OnCancelSlot(int slotIndex)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlotIndex(slotIndex)) return;
            if (resyncState[slotIndex] == STATE_NONE) return;

            resyncState[slotIndex] = STATE_NONE;
            MarkDirty();
            TickScheduler();
        }

        /// <summary>スロット未割当のクライアントが空きスロットを要求する（Late-Joiner 向けフォールバック）。</summary>
        [NetworkCallable]
        public void OnRequestSlot(int playerId)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (userPlayerId == null) return;
            if (VRCPlayerApi.GetPlayerById(playerId) == null) return;

            short pid = (short)playerId;

            // 既に割当済みならスキップ
            for (int i = 0; i < maxPlayers; i++)
                if (userPlayerId[i] == pid) return;

            for (int i = 0; i < maxPlayers; i++)
            {
                if (userPlayerId[i] == 0)
                {
                    InitializeSlot(i, pid);
                    MarkDirty();
                    if (debugLoggingEnabled)
                        LogMessage($"Fallback slot assigned: player {playerId} → slot {i}");
                    return;
                }
            }

            LogWarning($"OnRequestSlot: No empty slot for player {playerId}");
        }

        // =====================================================================
        //  スロット管理 (Design Section 13.1)
        // =====================================================================

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (userPlayerId == null) return;

            short playerId = (short)player.playerId;

            for (int i = 0; i < maxPlayers; i++)
            {
                if (userPlayerId[i] == playerId) return;
            }

            // 既にインスタンスにいないプレイヤーの残留スロットをクリーンアップ
            // （OnPlayerLeft のシリアライズがロストした場合のフォールバック）
            for (int i = 0; i < maxPlayers; i++)
            {
                if (userPlayerId[i] == 0) continue;
                VRCPlayerApi existing = VRCPlayerApi.GetPlayerById(userPlayerId[i]);
                if (existing == null || !existing.IsValid())
                {
                    if (debugLoggingEnabled)
                        LogMessage($"Stale slot {i} (player {userPlayerId[i]}) cleaned up");
                    ResetSlot(i);
                }
            }

            for (int i = 0; i < maxPlayers; i++)
            {
                if (userPlayerId[i] == 0)
                {
                    InitializeSlot(i, playerId);
                    MarkDirty();

                    if (debugLoggingEnabled)
                        LogMessage($"Player {playerId} → slot {i}");
                    return;
                }
            }

            LogWarning($"No empty slot for player {playerId}");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (userPlayerId == null) return;

            short playerId = (short)player.playerId;
            for (int i = 0; i < maxPlayers; i++)
            {
                if (userPlayerId[i] == playerId)
                {
                    ResetSlot(i);

                    // Rejoin 等でワールド破棄直前に呼ばれると遅延シリアライズが
                    // ロストするため、PlaybackMonitor と併せて即時送信する
                    CompressTimestamps();
                    RequestSerialization();
                    NotifyObservers();
                    _serializationPending = false;
                    if (playbackMonitor != null) playbackMonitor.FlushSerialization();

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
                _ownerTimestamp = new float[maxPlayers];

            float serverTime = GetServerTime();
            int total = 0;

            for (int i = 0; i < maxPlayers; i++)
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
            if (_timelineLogging) TL($"a=GLOBAL_RESYNC total={total}");
            LogMessage($"Global Resync triggered: {total} users queued");
        }

        /// <summary>全クライアントに Active 直接リブートを命じる。Resync では解消しない重度障害時の最終手段。</summary>
        public void TriggerGlobalForceReboot()
        {
            if (!TryTakeOwnership()) return;
            globalForceRebootSeq++;
            RequestSerialization();
            NotifyObservers();
            if (_timelineLogging) TL($"a=GLOBAL_FORCE_REBOOT_TRIGGER seq={globalForceRebootSeq}");
            LogMessage($"Global force reboot triggered: seq={globalForceRebootSeq}");
        }

        // =====================================================================
        //  Getters（UI パネル・Controller から参照）
        // =====================================================================

        /// <summary>指定スロットの Resync 状態を返す。クライアント側が Grant 検知に使う。</summary>
        public int GetResyncState(int slotIndex)
        {
            if (resyncState == null || slotIndex < 0 || slotIndex >= maxPlayers) return STATE_NONE;
            return resyncState[slotIndex];
        }

        /// <summary>指定スロットに割り当てられたプレイヤー ID を返す。</summary>
        public int GetUserPlayerId(int slotIndex)
        {
            if (userPlayerId == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0;
            return userPlayerId[slotIndex];
        }

        /// <summary>圧縮タイムスタンプを復元して返す。待ち時間推定や UI 表示に使う。</summary>
        public float GetUserTimestamp(int slotIndex)
        {
            if (userTimestampDelta == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0f;
            return userTimestampOffset - userTimestampDelta[slotIndex] * 0.1f;
        }

        public int GetMaxPlayers() { return maxPlayers; }
        public PlaybackMonitor GetPlaybackMonitor() { return playbackMonitor; }
        public int GetMaxConcurrentResyncUsers() { return maxConcurrentResyncUsers; }
        public int GetMaxConnectionLimit() { return maxConnectionLimit; }
        public int GetMinConnectionLimit() { return maxPlayers + 1; }
        public int GetMaxConnectionLimitCap() { return maxPlayers * 2; }
        public int GetGlobalForceRebootSeq() { return globalForceRebootSeq; }

        public int GetQueuedCount()
        {
            if (resyncState == null) return 0;
            int count = 0;
            for (int i = 0; i < maxPlayers; i++)
                if (resyncState[i] == STATE_QUEUED) count++;
            return count;
        }

        public int GetActiveResyncCount()
        {
            return CountGrantedOrRunning();
        }

        public int GetAssignedUserCount()
        {
            if (userPlayerId == null) return 0;
            int count = 0;
            for (int i = 0; i < maxPlayers; i++)
                if (userPlayerId[i] != 0) count++;
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
            if (resyncState == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0f;
            if (resyncState[slotIndex] != STATE_QUEUED) return 0f;

            float myTimestamp = GetUserTimestamp(slotIndex);
            int ahead = 0;
            for (int i = 0; i < maxPlayers; i++)
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
            for (int i = 0; i < maxPlayers; i++)
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
            for (int i = 0; i < maxPlayers; i++)
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

        /// <summary>スタッフが総接続数上限をランタイムで変更する。</summary>
        public void SetMaxConnectionLimitRuntime(int value)
        {
            if (!TryTakeOwnership()) return;
            maxConnectionLimit = (byte)Mathf.Clamp(value, maxPlayers + 1, maxPlayers * 2);
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

        /// <summary>スロットを完全にクリアし、PlaybackMonitor 側の状態も解除する。</summary>
        private void ResetSlot(int i)
        {
            if (playbackMonitor != null) playbackMonitor.ClearSlot(i);
            InitializeSlot(i, 0);
        }

        private bool ValidateSlotIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < maxPlayers
                && resyncState != null && resyncState.Length == maxPlayers;
        }

        private float GetServerTime()
        {
            return (float)Networking.GetServerTimeInSeconds();
        }

        /// <summary>スタッフ操作用: Ownership を取得する。失敗時は操作を中断させる。</summary>
        private bool TryTakeOwnership()
        {
            if (Networking.IsOwner(gameObject)) return true;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;

            Networking.SetOwner(local, gameObject);
            return Networking.IsOwner(gameObject);
        }

        private void LogMessage(string message)
        {
            Debug.Log($"[AunCast/ResyncCoordinator] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/ResyncCoordinator] {message}", this);
        }

        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={Networking.GetServerTimeInMilliseconds()} c=RC {eventAndData}");
        }
    }
}
