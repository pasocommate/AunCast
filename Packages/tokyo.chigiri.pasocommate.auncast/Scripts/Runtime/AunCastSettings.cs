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
        // AVProの出力解像度上限。帯域・GPU負荷のトレードオフで決める
        [Tooltip("AVProの最大解像度（px）")]
        public int maximumResolution = 720;

        // AVProの低遅延モード。ライブ配信向けだがデコードが不安定になりうる
        [Tooltip("AVProの低遅延モードを有効にする")]
        public bool useLowLatency = true;

        [Header("Gesture HUD")]
        [Tooltip("VR呼び出しジェスチャーの初期設定（ビットフラグ）。1=両手トリガー、2=右スティック上、4=左手ダブルトリガー、8=右手ダブルトリガー。")]
        public int defaultSummonGesture = 2;

        [Tooltip("デスクトップ呼び出しジェスチャーの初期設定（ビットフラグ）。1=Tabダブルタップ、2=F5ダブルタップ、4=ESC長押し。")]
        public int defaultDesktopSummonGesture = 1;

        // 誤操作防止のための長押し判定時間
        [Tooltip("長押しジェスチャーの保持時間（秒）。VR両手トリガー / 右スティック上 / デスクトップESCに共通適用。")]
        public float gestureHoldDuration = 0.8f;

        // 瞬間的な押下でHUDがちらつかないよう表示を遅延させる
        [Tooltip("HUDプログレスを表示し始めるまでの猶予（秒）。誤押下でちらつかないようにこれを過ぎてから表示する。")]
        public float gestureHudShowThreshold = 0.1f;

        [Tooltip("VR: HUDの頭部ローカル座標における配置オフセット（m）。(0,0,Z)で視界中央、Yを下げると視界下部に配置。")]
        public Vector3 hudVrLocalOffset = new Vector3(0f, 0f, 0.6f);

        [Tooltip("デスクトップ: HUDのカメラローカル座標における配置オフセット（m）。Zは前方距離。")]
        public Vector3 hudDesktopLocalOffset = new Vector3(0f, -0.1f, 0.6f);

        [Header("Volume")]
        [Tooltip("起動時のローカル再生デフォルト音量（0〜1）")]
        public float defaultVolume = 0.6f;

        [Header("Next URL Prefill")]
        // VRCUrl ではなく string で保管する。VRCUrl（ネスト [Serializable]）はプレハブインスタンス上で
        // オーバーライド追跡が不安定で「たまに空に戻る」ため。Udon コンポーネントへ転写する際に VRCUrl 化する。
        [Tooltip("Next URL欄の初期値として表示する配信URL。空欄なら未設定。")]
        public string defaultUrl = "";

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

        [Tooltip("パスコードなしでスタッフ権限が付与される VRChat ユーザー名のリスト。空の場合はパスコード解錠のみ有効。")]
        public string[] staffAllowedUserNames = new string[0];

        [Header("Silence Detection")]
        [Tooltip("無音判定RMS閾値 (dBFS)")]
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

        [Tooltip("スタッフが変更できるドリフト Resync 閾値の固定段階インデックス")]
        // Runtime 設定 assembly から Udon assembly は参照できないため、100 ms の列挙値 (1) を保持する。
        public int driftResyncThresholdIndex = 1;

        [Tooltip("ドリフトEMAの時定数（秒）。大きいほど緩やかに追従する")]
        public float driftSmoothingTimeConstant = 1.5f;

        [Tooltip("安定再生開始直後にドリフト積算を抑制する猶予時間（秒）")]
        public float driftWarmupSec = 5.0f;

        [Header("Resync Coordinator")]
        [Tooltip("無音検知による自動 Resync（Silence Resync）を初期状態で有効にする。各クライアントのローカルトグルの初期値。")]
        public bool defaultAutoSilenceResync = true;

        [Tooltip("同時Resync上限の初期値")]
        public byte maxConcurrentResyncUsers = 10;

        [Tooltip("配信サーバへの同時接続上限の既定値")]
        public byte maxConnectionLimit = 100;

        [Tooltip("Grant 後の接続開始タイムアウト（秒）")]
        public float grantTimeoutSec = 10.0f;

        [Tooltip("Running 状態の最大継続時間（秒）")]
        public float runningTimeoutSec = 50.0f;

        [Header("Resync Client")]
        [Tooltip("GRANTED 後、切替完了までの最大許容時間（秒）")]
        public float resyncCycleTimeoutSec = 45.0f;

        [Tooltip("LoadURL完了後のクールダウン（秒）")]
        public float localCooldownSec = 6.5f;

        [Tooltip("再試行の基本待機時間（秒）")]
        public float baseCooldownSec = 5.0f;

        [Tooltip("再試行間隔を連続失敗ごとに増やす倍率")]
        public float retryCooldownMultiplier = 1.5f;

        [Tooltip("再試行の最大待機時間（秒）")]
        public float maxRetryCooldownSec = 90.0f;

        [Header("Crossfade")]
        [Tooltip("クロスフェード時間（秒）")]
        public float crossfadeDurationSec = 0.1f;

        [Tooltip("映像テクスチャが取得できなくても、再生時刻が前進している場合は音声のみ配信として切替を完了する")]
        public bool allowAudioOnlyFallback = true;

        [Tooltip("音声のみ配信として扱うまで、Standby の映像テクスチャ到着を待つ時間（秒）")]
        public float audioOnlyFallbackDelaySec = 3.0f;

        [Tooltip("音声のみ配信として扱うために必要な Standby の再生時刻前進量（秒）")]
        public float audioOnlyFallbackMinAdvanceSec = 0.5f;

        [Header("Screen")]
        // 再生していない間（停止中）にスクリーンへ表示する固定画像。
        // 未指定の場合は、各スクリーンのマテリアル / RawImage に初期割り当てされていたテクスチャへ復元する。
        [Tooltip("再生停止中にスクリーンへ表示する固定画像。未指定なら初期割り当てのテクスチャへ復元する。")]
        public Texture2D idleScreenTexture;
    }
}
