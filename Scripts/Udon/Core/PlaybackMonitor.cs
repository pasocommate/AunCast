
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
    public class PlaybackMonitor : UdonSharpBehaviour
    {
        [Header("Settings")]
        [Tooltip("最大プレイヤー数（ResyncCoordinator と同じ値を設定）")]
        [SerializeField] private int maxPlayers = 82;

        [Tooltip("デバッグログを有効にする")]
        [SerializeField] private bool debugLoggingEnabled = false;

        [Header("References")]
        [Tooltip("同期変数の更新を通知する UI パネル（描画再更新用）")]
        [SerializeField] private StaffControlPanel staffPanel;

        [UdonSynced] private byte[] playbackActive;
        [UdonSynced] private byte[] connectingActive;
        [UdonSynced] private byte[] errorActive;

        /// <summary>同一フレーム内の複数 RPC を 1 回のシリアライズにまとめるためのダーティフラグ。</summary>
        private bool _serializationPending;

        /// <summary>maxPlayers ビットを格納するのに必要なバイト数（ceil(maxPlayers/8)）。</summary>
        private int _packedLength;

        /// <summary>0-255 の各値に対するセットビット数を事前計算したルックアップテーブル。CountBits で使用。</summary>
        private byte[] _popcount;

        private void Start()
        {
            // ビットパック配列を確保（同期変数は null か長さ不一致なら再初期化）
            _packedLength = (maxPlayers + 7) / 8;
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

        // =====================================================================
        //  playbackActive 内部アクセス
        // =====================================================================

        /// <summary>指定スロットの再生ビットを取得する。</summary>
        private bool GetSlotActive(int slotIndex)
        {
            return (playbackActive[slotIndex >> 3] & (1 << (slotIndex & 7))) != 0;
        }

        /// <summary>指定スロットの再生ビットを設定する。変化があれば true を返す。</summary>
        private bool SetSlotActive(int slotIndex, bool value)
        {
            int byteIdx = slotIndex >> 3;
            byte mask = (byte)(1 << (slotIndex & 7));
            byte old = playbackActive[byteIdx];
            byte next = value ? (byte)(old | mask) : (byte)(old & ~mask);
            if (old == next) return false;
            playbackActive[byteIdx] = next;
            return true;
        }

        /// <summary>指定スロットの接続中ビットを取得する。</summary>
        private bool GetSlotConnecting(int slotIndex)
        {
            return (connectingActive[slotIndex >> 3] & (1 << (slotIndex & 7))) != 0;
        }

        /// <summary>指定スロットの接続中ビットを設定する。変化があれば true を返す。</summary>
        private bool SetSlotConnecting(int slotIndex, bool value)
        {
            int byteIdx = slotIndex >> 3;
            byte mask = (byte)(1 << (slotIndex & 7));
            byte old = connectingActive[byteIdx];
            byte next = value ? (byte)(old | mask) : (byte)(old & ~mask);
            if (old == next) return false;
            connectingActive[byteIdx] = next;
            return true;
        }

        /// <summary>指定スロットのエラービットを取得する。</summary>
        private bool GetSlotError(int slotIndex)
        {
            return (errorActive[slotIndex >> 3] & (1 << (slotIndex & 7))) != 0;
        }

        /// <summary>指定スロットのエラービットを設定する。変化があれば true を返す。</summary>
        private bool SetSlotError(int slotIndex, bool value)
        {
            int byteIdx = slotIndex >> 3;
            byte mask = (byte)(1 << (slotIndex & 7));
            byte old = errorActive[byteIdx];
            byte next = value ? (byte)(old | mask) : (byte)(old & ~mask);
            if (old == next) return false;
            errorActive[byteIdx] = next;
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
                Debug.Log($"[AunCast/PlaybackMonitor] Sent playback report: slot {slotIndex} active={isActive}", this);
        }

        /// <summary>エラー状態を Owner に報告する（クライアントから呼ぶ）。</summary>
        public void ReportErrorForSlot(int slotIndex, bool isError)
        {
            if (!ValidateSlot(slotIndex)) return;

            int encoded = isError ? 1 : 0;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(OnReportError), slotIndex, encoded);

            if (debugLoggingEnabled)
                Debug.Log($"[AunCast/PlaybackMonitor] Sent error report: slot {slotIndex} error={isError}", this);
        }

        /// <summary>接続試行中状態を Owner に報告する（クライアントから呼ぶ）。</summary>
        public void ReportConnectingForSlot(int slotIndex, bool isConnecting)
        {
            if (!ValidateSlot(slotIndex)) return;

            int encoded = isConnecting ? 1 : 0;
            SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(OnReportConnecting), slotIndex, encoded);

            if (debugLoggingEnabled)
                Debug.Log($"[AunCast/PlaybackMonitor] Sent connecting report: slot {slotIndex} connecting={isConnecting}", this);
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
                    Debug.Log($"[AunCast/PlaybackMonitor] Slot {slotIndex} playback={active != 0}", this);
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
                    Debug.Log($"[AunCast/PlaybackMonitor] Slot {slotIndex} error={error != 0}", this);
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
                    Debug.Log($"[AunCast/PlaybackMonitor] Slot {slotIndex} connecting={connecting != 0}", this);
            }
        }

        // =====================================================================
        //  Owner 直接呼び出し（ResyncCoordinator.ResetSlot 用）
        // =====================================================================

        /// <summary>スロットの再生状態をクリアする。Owner から直接呼ぶ。</summary>
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
            _packedLength = (maxPlayers + 7) / 8;
            NotifyObservers();
        }

        /// <summary>StaffControlPanel にステータス変化を通知して表示を更新させる。</summary>
        private void NotifyObservers()
        {
            if (staffPanel != null) staffPanel.OnCoordinatorChanged();
        }

        // =====================================================================
        //  Getter
        // =====================================================================

        /// <summary>現在再生中のスロット総数を返す。ResyncCoordinator の同時接続上限判定に使用。</summary>
        public int GetPlayingEstimateCount()
        {
            return CountBits(playbackActive);
        }

        /// <summary>現在接続試行中のスロット総数を返す。接続上限スケジューリングに使用。</summary>
        public int GetConnectingEstimateCount()
        {
            return CountBits(connectingActive);
        }

        /// <summary>指定スロットが再生中か返す（StaffControlPanel のインジケータ表示用）。</summary>
        public int GetPlaybackActive(int slotIndex)
        {
            if (playbackActive == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0;
            return GetSlotActive(slotIndex) ? 1 : 0;
        }

        /// <summary>指定スロットが接続試行中か返す（StaffControlPanel のインジケータ表示用）。</summary>
        public int GetConnectingActive(int slotIndex)
        {
            if (connectingActive == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0;
            return GetSlotConnecting(slotIndex) ? 1 : 0;
        }

        /// <summary>指定スロットがエラー状態か返す（StaffControlPanel のインジケータ表示用）。</summary>
        public int GetErrorActive(int slotIndex)
        {
            if (errorActive == null || slotIndex < 0 || slotIndex >= maxPlayers) return 0;
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

        /// <summary>スロット範囲と配列整合性を検証する。初期化前や不正インデックスからの保護。</summary>
        private bool ValidateSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < maxPlayers
                && playbackActive != null && playbackActive.Length == _packedLength
                && connectingActive != null && connectingActive.Length == _packedLength
                && errorActive != null && errorActive.Length == _packedLength;
        }
    }
}
