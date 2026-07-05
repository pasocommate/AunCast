
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// 各ユーザーの再生状態を同期するモニタリング専用オブジェクト。
    /// Owner が一元管理し、クライアントは NetworkCallable RPC で報告する。
    /// Ownership 移転を排除し、複数クライアントの報告をバッチ serialize する。
    /// 1 スロット 1 ビットにパックして同期帯域を節約する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AunCastPlaybackMonitor : UdonSharpBehaviour
    {
        // AunCastResyncCoordinator.MAX_PLAYERS と同値に保つこと。スロット数が一致しないと
        // 本クラスのビットパック配列長と Coordinator の配列長が食い違う。
        private const int MAX_PLAYERS = 82;

        [Header("Settings")]
        [Tooltip("デバッグログを有効にする")]
        [SerializeField] private bool debugLoggingEnabled = false;

        [Header("References")]
        [Tooltip("同期変数の更新を通知する先（AunCastStaffControlPanel を配線）。UI 具象型に依存しないため UdonSharpBehaviour で受ける。")]
        [SerializeField] private UdonSharpBehaviour staffNotifyTarget;
        [Tooltip("スロット→プレイヤー ID 対応の参照元。OnPlayerLeft で残留ビットを掃除するときに使う。")]
        [SerializeField] private AunCastResyncCoordinator coordinator;

        [UdonSynced] private byte[] playbackActive;
        [UdonSynced] private byte[] connectingActive;
        [UdonSynced] private byte[] errorActive;

        /// <summary>同一フレーム内の複数 RPC を 1 回のシリアライズにまとめるためのダーティフラグ。</summary>
        private bool _serializationPending;

        /// <summary>MAX_PLAYERS ビットを格納するのに必要なバイト数（ceil(MAX_PLAYERS/8)）。</summary>
        private int _packedLength;

        /// <summary>0-255 の各値に対するセットビット数を事前計算したルックアップテーブル。CountBits で使用。</summary>
        private byte[] _popcount;

        private void Start()
        {
            // ビットパック配列を確保（同期変数は null か長さ不一致なら再初期化）
            _packedLength = (MAX_PLAYERS + 7) / 8;
            if (playbackActive == null || playbackActive.Length != _packedLength)
                playbackActive = new byte[_packedLength];
            if (connectingActive == null || connectingActive.Length != _packedLength)
                connectingActive = new byte[_packedLength];
            if (errorActive == null || errorActive.Length != _packedLength)
                errorActive = new byte[_packedLength];

            // popcount テーブル構築（ループ内で毎回ビット数えるより O(1) 参照で高速化）
            _popcount = new byte[256];
            for (int i = 1; i < 256; i++)
                _popcount[i] = (byte)(_popcount[i >> 1] + (i & 1));
        }

        /// <summary>フレーム末にダーティフラグを確認し、まとめて 1 回だけシリアライズを発行する。</summary>
        private void Update()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!_serializationPending) return;

            RequestSerialization();
            NotifyObservers();
            _serializationPending = false;
        }

        /// <summary>遅延シリアライズを即時送信する。OnPlayerLeft 等ワールド破棄直前に呼ぶ。</summary>
        public void FlushSerialization()
        {
            if (!_serializationPending) return;
            RequestSerialization();
            NotifyObservers();
            _serializationPending = false;
        }

        /// <summary>
        /// プレイヤー退室時に、自オブジェクトの所有者が残留ビットを掃除する。
        /// AunCastResyncCoordinator と所有者が分かれていても確実にビットを解放するため、
        /// 自前で全スロットを走査して 3 配列まとめてクリアする。
        /// </summary>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (CleanupStaleSlots() && debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Cleared stale slots on player left (playerId={player.playerId})", this);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (CleanupStaleSlots() && debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Cleared stale slots on ownership transferred", this);
        }

        /// <summary>
        /// 参加時のフォールバック掃除。OnPlayerLeft のシリアライズがロストして残った
        /// 過去のビットを、新規プレイヤーの参加時にまとめて回収する。
        /// </summary>
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (CleanupStaleSlots() && debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Cleared stale slots on player joined (playerId={player.playerId})", this);
        }

        /// <summary>
        /// 全スロットを走査し「対応プレイヤーが既にインスタンスにいない」3 配列のビットをクリアする。
        /// 自オブジェクトの所有者のみ書き換え、即時シリアライズして他クライアントへ反映する。
        /// </summary>
        private bool CleanupStaleSlots()
        {
            if (!Networking.IsOwner(gameObject)) return false;
            if (coordinator == null) return false;
            if (playbackActive == null || playbackActive.Length != _packedLength) return false;

            bool anyChanged = false;
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                // 3 配列とも 0 のスロットは検証不要
                if (!GetSlotActive(i) && !GetSlotConnecting(i) && !GetSlotError(i)) continue;

                // pid==0 は Coordinator 側で既にスロット解放済み = プレイヤー不在。
                // pid!=0 でも GetPlayerById が null / IsValid()=false なら退室済み。
                int pid = coordinator.GetUserPlayerId(i);
                VRCPlayerApi p = pid == 0 ? null : VRCPlayerApi.GetPlayerById(pid);
                if (p != null && p.IsValid()) continue;

                bool changed = SetSlotActive(i, false);
                changed |= SetSlotConnecting(i, false);
                changed |= SetSlotError(i, false);
                if (changed) anyChanged = true;
            }

            if (!anyChanged) return false;

            // ワールド破棄直前に呼ばれることもあるため遅延せず即時送信する
            _serializationPending = true;
            FlushSerialization();
            return true;
        }

        // =====================================================================
        //  スロット別ビットアクセス（playbackActive / connectingActive / errorActive）
        // =====================================================================

        private bool GetSlotActive(int slotIndex)    => GetBit(playbackActive, slotIndex);
        private bool GetSlotConnecting(int slotIndex) => GetBit(connectingActive, slotIndex);
        private bool GetSlotError(int slotIndex)      => GetBit(errorActive, slotIndex);

        private bool SetSlotActive(int slotIndex, bool value)    => SetBit(playbackActive, slotIndex, value);
        private bool SetSlotConnecting(int slotIndex, bool value) => SetBit(connectingActive, slotIndex, value);
        private bool SetSlotError(int slotIndex, bool value)      => SetBit(errorActive, slotIndex, value);

        /// <summary>ビットパック配列の指定スロットを読み取る。</summary>
        private bool GetBit(byte[] array, int slotIndex)
        {
            return (array[slotIndex >> 3] & (1 << (slotIndex & 7))) != 0;
        }

        /// <summary>ビットパック配列の指定スロットを書き込む。変化があれば true を返す。</summary>
        private bool SetBit(byte[] array, int slotIndex, bool value)
        {
            int byteIdx = slotIndex >> 3;
            byte mask = (byte)(1 << (slotIndex & 7));
            byte old = array[byteIdx];
            byte next = value ? (byte)(old | mask) : (byte)(old & ~mask);
            if (old == next) return false;
            array[byteIdx] = next;
            return true;
        }

        // =====================================================================
        //  クライアント → Owner RPC
        // =====================================================================

        /// <summary>ローカル再生状態を Owner に報告する（クライアントから呼ぶ）。</summary>
        public void ReportForSlot(int slotIndex, bool isActive)
        {
            if (!ValidateSlot(slotIndex)) return;

            int encoded = isActive ? 1 : 0;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(OnReportPlayback), slotIndex, encoded);

            if (debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Sent playback report: slot {slotIndex} active={isActive}", this);
        }

        /// <summary>エラー状態を Owner に報告する（クライアントから呼ぶ）。</summary>
        public void ReportErrorForSlot(int slotIndex, bool isError)
        {
            if (!ValidateSlot(slotIndex)) return;

            int encoded = isError ? 1 : 0;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(OnReportError), slotIndex, encoded);

            if (debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Sent error report: slot {slotIndex} error={isError}", this);
        }

        /// <summary>接続試行中状態を Owner に報告する（クライアントから呼ぶ）。</summary>
        public void ReportConnectingForSlot(int slotIndex, bool isConnecting)
        {
            if (!ValidateSlot(slotIndex)) return;

            int encoded = isConnecting ? 1 : 0;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(OnReportConnecting), slotIndex, encoded);

            if (debugLoggingEnabled)
                Debug.Log($"[AunCast/AunCastPlaybackMonitor] Sent connecting report: slot {slotIndex} connecting={isConnecting}", this);
        }

        // =====================================================================
        //  [NetworkCallable] ハンドラ — Owner 側で受信
        // =====================================================================

        /// <summary>Owner 側で再生状態 RPC を受信し、ビット配列を更新してダーティマークする。</summary>
        [NetworkCallable]
        public void OnReportPlayback(int slotIndex, int active)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlot(slotIndex)) return;

            if (SetSlotActive(slotIndex, active != 0))
            {
                _serializationPending = true;

                if (debugLoggingEnabled)
                    Debug.Log($"[AunCast/AunCastPlaybackMonitor] Slot {slotIndex} playback={active != 0}", this);
            }
        }

        /// <summary>Owner 側でエラー状態 RPC を受信し、ビット配列を更新してダーティマークする。</summary>
        [NetworkCallable]
        public void OnReportError(int slotIndex, int error)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlot(slotIndex)) return;

            if (SetSlotError(slotIndex, error != 0))
            {
                _serializationPending = true;

                if (debugLoggingEnabled)
                    Debug.Log($"[AunCast/AunCastPlaybackMonitor] Slot {slotIndex} error={error != 0}", this);
            }
        }

        /// <summary>Owner 側で接続中状態 RPC を受信し、ビット配列を更新してダーティマークする。</summary>
        [NetworkCallable]
        public void OnReportConnecting(int slotIndex, int connecting)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!ValidateSlot(slotIndex)) return;

            if (SetSlotConnecting(slotIndex, connecting != 0))
            {
                _serializationPending = true;

                if (debugLoggingEnabled)
                    Debug.Log($"[AunCast/AunCastPlaybackMonitor] Slot {slotIndex} connecting={connecting != 0}", this);
            }
        }

        // =====================================================================
        //  Owner 直接呼び出し
        // =====================================================================

        /// <summary>
        /// スロットの 3 配列ビットをまとめてクリアする。自オブジェクトの所有者が呼ぶ前提。
        /// 通常の退室掃除は OnPlayerLeft / OnPlayerJoined で自動実行されるため、本メソッドは
        /// 個別スロットを明示的に解放したい場面（テスト等）でのみ使用する。
        /// </summary>
        public void ClearSlot(int slotIndex)
        {
            if (!ValidateSlot(slotIndex)) return;

            bool changed = SetSlotActive(slotIndex, false);
            changed |= SetSlotConnecting(slotIndex, false);
            changed |= SetSlotError(slotIndex, false);
            if (changed)
                _serializationPending = true;
        }

        // =====================================================================
        //  同期コールバック
        // =====================================================================

        /// <summary>リモートクライアントが同期データを受信した際、UI 再描画を通知する。</summary>
        public override void OnDeserialization()
        {
            _packedLength = (MAX_PLAYERS + 7) / 8;
            NotifyObservers();
        }

        /// <summary>AunCastStaffControlPanel にステータス変化を通知して表示を更新させる。</summary>
        private void NotifyObservers()
        {
            if (staffNotifyTarget != null) staffNotifyTarget.SendCustomEvent("OnCoordinatorChanged");
        }

        // =====================================================================
        //  Getter
        // =====================================================================

        /// <summary>現在再生中のスロット総数を返す。AunCastResyncCoordinator の同時接続上限判定に使用。</summary>
        public int GetPlayingEstimateCount()
        {
            return CountAssignedBits(playbackActive);
        }

        /// <summary>現在接続試行中のスロット総数を返す。接続上限スケジューリングに使用。</summary>
        public int GetConnectingEstimateCount()
        {
            return CountAssignedBits(connectingActive);
        }

        /// <summary>指定スロットが再生中か返す（AunCastStaffControlPanel のインジケータ表示用）。</summary>
        public int GetPlaybackActive(int slotIndex)
        {
            if (playbackActive == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0;
            return GetSlotActive(slotIndex) ? 1 : 0;
        }

        /// <summary>指定スロットが接続試行中か返す（AunCastStaffControlPanel のインジケータ表示用）。</summary>
        public int GetConnectingActive(int slotIndex)
        {
            if (connectingActive == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0;
            return GetSlotConnecting(slotIndex) ? 1 : 0;
        }

        /// <summary>指定スロットがエラー状態か返す（AunCastStaffControlPanel のインジケータ表示用）。</summary>
        public int GetErrorActive(int slotIndex)
        {
            if (errorActive == null || slotIndex < 0 || slotIndex >= MAX_PLAYERS) return 0;
            return GetSlotError(slotIndex) ? 1 : 0;
        }

        // =====================================================================
        //  ユーティリティ
        // =====================================================================

        /// <summary>ルックアップテーブルを使い、バイト配列全体のセットビット数を高速に合計する。</summary>
        private int CountBits(byte[] packed)
        {
            if (packed == null) return 0;
            int count = 0;
            for (int i = 0; i < packed.Length; i++)
                count += _popcount[packed[i]];
            return count;
        }

        /// <summary>
        /// Coordinator で現在割り当て済みのスロットだけを数える。
        /// 未割当スロットの残留ビットを Playing/Connecting サマリに混ぜないため。
        /// </summary>
        private int CountAssignedBits(byte[] packed)
        {
            if (packed == null) return 0;
            if (coordinator == null) return CountBits(packed);

            int count = 0;
            int limit = Mathf.Min(MAX_PLAYERS, packed.Length * 8);
            for (int i = 0; i < limit; i++)
            {
                // 立っているビットだけを対象にし、外部参照（GetUserPlayerId）の呼び出しを最小化する
                if (!GetBit(packed, i)) continue;
                if (coordinator.GetUserPlayerId(i) == 0) continue;
                count++;
            }
            return count;
        }

        /// <summary>スロット範囲と配列整合性を検証する。初期化前や不正インデックスからの保護。</summary>
        private bool ValidateSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < MAX_PLAYERS
                && playbackActive != null && playbackActive.Length == _packedLength
                && connectingActive != null && connectingActive.Length == _packedLength
                && errorActive != null && errorActive.Length == _packedLength;
        }
    }
}
