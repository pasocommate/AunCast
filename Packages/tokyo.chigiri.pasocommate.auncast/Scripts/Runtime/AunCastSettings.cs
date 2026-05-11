using UnityEngine;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// エディタ時専用のプロジェクト設定。セットアップツールがシーン生成時に参照し、
    /// 各コンポーネントへ値を転写する。IEditorOnly のためビルドには含まれない。
    /// </summary>
    [DisallowMultipleComponent]
    public class AunCastSettings : MonoBehaviour, IEditorOnly
    {
        // AVPro の出力解像度上限。帯域・GPU 負荷のトレードオフで決める
        [Tooltip("AVPro の最大解像度（px）")]
        public int maximumResolution = 720;

        // AVPro の低遅延モード。ライブ配信向けだがデコードが不安定になりうる
        [Tooltip("AVPro の低遅延モードを有効にする")]
        public bool useLowLatency = true;

        [Header("Gesture HUD")]
        // 誤操作防止のための長押し判定時間
        [Tooltip("長押しジェスチャーの保持時間（秒）。VR 両手トリガー / 右スティック上 / デスクトップ ESC に共通適用。")]
        public float gestureHoldDuration = 0.8f;

        // 瞬間的な押下で HUD がちらつかないよう表示を遅延させる
        [Tooltip("HUD プログレスを表示し始めるまでの猶予（秒）。誤押下でちらつかないようにこれを過ぎてから表示する。")]
        public float gestureHudShowThreshold = 0.1f;

        [Header("Portable Panel Auto Dismiss")]
        // パネルから離れすぎたら自動で閉じる
        [Tooltip("ポータブルパネルからこの距離（m）以上離れると自動的に閉じる。0 で無効。")]
        public float panelAutoDismissDistance = 3f;

        // パネルが視界外に出てから一定時間で閉じる
        [Tooltip("ポータブルパネルが視界外に出てからこの秒数経過で自動的に閉じる。0 で無効。")]
        public float panelOutOfSightDismissSec = 20f;

        [Header("Wall Panel Distance View")]
        // 壁パネルに近づくとフル表示、離れると Resync のみ表示に切り替わるシュミットトリガー
        [Tooltip("この距離（m）以内に近づくとフルコンテンツ表示に切り替える（内側閾値）")]
        public float wallNearDistance = 2.8f;

        [Tooltip("この距離（m）以上離れると Resync のみ表示に切り替える（外側閾値）")]
        public float wallFarDistance = 3f;

        [Header("Wall Panel Staff Unlock")]
        [Tooltip("Staff ビュー解錠用の 4 桁数字パスコード。空文字で無効。")]
        public string wallUnlockPasscode = "0000";

        [Header("Silence Detection")]
        [Tooltip("無音判定 RMS 閾値 (dBFS)")]
        public float silenceRmsThresholdDbfs = -60f;

        [Tooltip("無音判定の継続時間（秒）")]
        public float silenceConsecutiveSec = 2.0f;

        [Tooltip("Resync 後に無音検知を再有効化するまでの抑止時間（秒）")]
        public float silenceSuppressSec = 150.0f;

        [Tooltip("RMSメーターのピーク保持時間（秒）")]
        public float silenceMeterPeakHoldSec = 0.5f;

        [Tooltip("RMSメーターのピーク減衰速度（dB/秒）")]
        public float silenceMeterPeakDecayDbPerSec = 12.0f;

        [Header("Active Player Monitoring")]
        [Tooltip("停止判定の継続時間（秒）")]
        public float stalledTimeoutSec = 2.0f;

        [Tooltip("監視ポーリング間隔（秒）")]
        public float monitorIntervalSec = 0.1f;

        [Tooltip("GetTime() の最小前進量（秒）")]
        public float minAdvanceThresholdSec = 0.01f;

        [Tooltip("生存確認に必要な連続前進回数")]
        public int minConsecutiveAdvances = 5;

        [Header("Drift Detection")]
        [Tooltip("蓄積ドリフトがこの値（秒）を超えたら自動 Resync")]
        public float driftResyncThresholdSec = 0.1f;

        [Tooltip("ドリフト EMA の時定数（秒）。大きいほど緩やかに追従する")]
        public float driftSmoothingTimeConstant = 1.5f;

        [Tooltip("再生開始直後にドリフト積算を抑制する猶予時間（秒）")]
        public float driftWarmupSec = 5.0f;

        [Header("Resync Coordinator")]
        [Tooltip("同時 Resync 実行数の初期上限")]
        public byte maxConcurrentResyncUsers = 10;

        [Tooltip("配信サーバへの総接続数の初期上限（0 = 無制限）")]
        public byte maxConnectionLimit = 0;

        [Tooltip("Grant 後の接続開始タイムアウト（秒）")]
        public float grantTimeoutSec = 10.0f;

        [Tooltip("Running 状態の最大継続時間（秒）")]
        public float runningTimeoutSec = 50.0f;

        [Header("Resync Client")]
        [Tooltip("GRANTED 後、切替完了までの最大許容時間（秒）")]
        public float resyncCycleTimeoutSec = 45.0f;

        [Tooltip("LoadURL 完了後のクールダウン（秒）")]
        public float localCooldownSec = 5.0f;

        [Tooltip("再試行の基本待機時間（秒）")]
        public float baseCooldownSec = 15.0f;

        [Tooltip("再試行の最大待機時間（秒）")]
        public float maxRetryCooldownSec = 120.0f;

        [Header("Reboot / Stall")]
        [Tooltip("リブートボタン表示条件: GetTime 停止超過時間（秒）")]
        public float rebootStallSec = 10.0f;

        [Header("Crossfade")]
        [Tooltip("クロスフェード時間（秒）")]
        public float crossfadeDurationSec = 0.1f;
    }
}
