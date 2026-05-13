
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// Active/Standby の PlayerManager 切替とクロスフェードを担当するコンポーネント。
    /// LocalDualPlayerController と同一 GameObject に配置される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class PlaybackSwitcher : UdonSharpBehaviour
    {
        // =================================================================
        //  Inspector 参照
        // =================================================================
        [SerializeField] private VideoPlayerManager playerManagerA;
        [SerializeField] private VideoPlayerManager playerManagerB;
        [SerializeField] private AudioSilenceDetector silenceDetectorA;
        [SerializeField] private AudioSilenceDetector silenceDetectorB;
        [SerializeField] private VideoMeshScreen[] meshScreens;
        [SerializeField] private VideoUiScreen[] uiScreens;
        [SerializeField] private UdonSharpBehaviour audioLinkBehaviour;

        // =================================================================
        //  Inspector パラメータ
        // =================================================================
        [Header("Crossfade")]
        [Tooltip("クロスフェード時間（秒）")]
        [SerializeField] private float crossfadeDurationSec = 0.3f;

        [Header("Debug")]
        [Tooltip("要所ログを詳細出力する")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Timeline")]
        [Tooltip("タイムラインログを出力する")]
        [SerializeField] private bool _timelineLogging;

        // =================================================================
        //  ローカル状態
        // =================================================================

        /// <summary>現在どちらのプレイヤーが視聴者へ出力中か (true=A, false=B)</summary>
        private bool _activeIsA = true;

        /// <summary>クロスフェード補間の起点タイムスタンプ</summary>
        private float _crossfadeStartedAt;

        /// <summary>重複代入を避けるための前回テクスチャキャッシュ</summary>
        private Texture _lastAssignedRenderTexture;

        /// <summary>null テクスチャ警告のスロットル用タイムスタンプ</summary>
        private float _lastNullTextureWarnAt;

        // =================================================================
        //  Active/Standby 取得
        // =================================================================

        /// <summary>PlayerManager A への直接参照（初期化・デバッグ用）。</summary>
        public VideoPlayerManager GetPlayerManagerA() => playerManagerA;
        /// <summary>PlayerManager B への直接参照（初期化・デバッグ用）。</summary>
        public VideoPlayerManager GetPlayerManagerB() => playerManagerB;

        /// <summary>現在視聴者に出力中のプレイヤーを返す。</summary>
        public VideoPlayerManager GetActiveManager()
        {
            return _activeIsA ? playerManagerA : playerManagerB;
        }

        /// <summary>次回切替用に待機中のプレイヤーを返す。</summary>
        public VideoPlayerManager GetStandbyManager()
        {
            return _activeIsA ? playerManagerB : playerManagerA;
        }

        /// <summary>Active 側に対応する無音検知器を返す。</summary>
        public AudioSilenceDetector GetActiveSilenceDetector()
        {
            return _activeIsA ? silenceDetectorA : silenceDetectorB;
        }

        /// <summary>Standby 側に対応する無音検知器を返す。</summary>
        public AudioSilenceDetector GetStandbySilenceDetector()
        {
            return _activeIsA ? silenceDetectorB : silenceDetectorA;
        }

        /// <summary>外部から Active 判定を参照するためのアクセサ。</summary>
        public bool GetActiveIsA()
        {
            return _activeIsA;
        }

        // =================================================================
        //  初期化
        // =================================================================

        private void Start()
        {
            DisableUnusedSilenceDetectors();
        }

        /// <summary>
        /// 初期状態を確立する: A をフル音量で Active、B をミュートで Standby に設定。
        /// </summary>
        public void InitializeToA()
        {
            _activeIsA = true;
            if (playerManagerA != null) playerManagerA.SetFadeGain(1.0f);
            if (playerManagerB != null) playerManagerB.SetFadeGain(0.0f);
        }

        /// <summary>
        /// 緊急停止: 両プレイヤーを停止し、A-Active 状態にリセットする。
        /// 復旧不能な異常時のフォールバック手段。
        /// </summary>
        public void ResetBothPlayersToA()
        {
            if (playerManagerA != null)
            {
                playerManagerA.Stop();
                playerManagerA.SetFadeGain(1.0f);
            }
            if (playerManagerB != null)
            {
                playerManagerB.Stop();
                playerManagerB.SetFadeGain(0.0f);
            }
            _activeIsA = true;
        }

        private void DisableUnusedSilenceDetectors()
        {
            DisableUnusedSilenceDetectorsForManager(playerManagerA, silenceDetectorA, "A");
            DisableUnusedSilenceDetectorsForManager(playerManagerB, silenceDetectorB, "B");
        }

        private void DisableUnusedSilenceDetectorsForManager(
            VideoPlayerManager manager,
            AudioSilenceDetector keepDetector,
            string label)
        {
            if (manager == null || manager.audioSources == null) return;

            int disabledCount = 0;
            for (int i = 0; i < manager.audioSources.Length; i++)
            {
                AudioSource source = manager.audioSources[i];
                if (source == null) continue;

                AudioSilenceDetector detector = source.GetComponent<AudioSilenceDetector>();
                if (detector == null) continue;
                if (detector == keepDetector) continue;
                if (!detector.enabled) continue;

                detector.enabled = false;
                disabledCount++;
            }

            if (disabledCount > 0)
                LogVerbose($"Disabled unused AudioSilenceDetector on Player{label}: {disabledCount}");
        }

        // =================================================================
        //  Standby 接続開始
        // =================================================================

        /// <summary>
        /// Standby プレイヤーで URL の読み込みを開始する。
        /// ホットスワップ準備として、Active 再生中に裏で接続を確立する。
        /// </summary>
        public void StartStandbyConnect(float now, VRC.SDKBase.VRCUrl url)
        {
            VideoPlayerManager standbyManager = GetStandbyManager();
            if (standbyManager != null)
            {
                // 接続中は音量ゼロで待機
                standbyManager.SetFadeGain(0.0f);
                standbyManager.LoadURL(url);
            }
            if (_timelineLogging) TL($"a=STANDBY_CONNECT");
            LogMessage($"Standby connect started (url={url.Get()})");
        }

        // =================================================================
        //  クロスフェード
        // =================================================================

        /// <summary>
        /// クロスフェードを開始する。映像は瞬時に Standby 側へ切替え、
        /// 音声のみ徐々にフェードさせることで視聴者の違和感を最小化する。
        /// </summary>
        public void StartCrossfade(float now)
        {
            _crossfadeStartedAt = now;

            // 映像は即座に新ソースへ切替（映像のクロスフェードは視覚的に不自然なため）
            UpdateRenderTextureFromManager(GetStandbyManager());

            // ゲインを初期値にリセットしてから TickCrossfade で漸次変化させる
            VideoPlayerManager activeManager = GetActiveManager();
            VideoPlayerManager standbyManager = GetStandbyManager();
            if (activeManager != null) activeManager.SetFadeGain(1.0f);
            if (standbyManager != null) standbyManager.SetFadeGain(0.0f);

            if (_timelineLogging) TL($"a=CROSSFADE_START");
            LogMessage("Switching: crossfade started");
        }

        /// <summary>
        /// クロスフェードを1フレーム分進める。
        /// 等パワーカーブ (sin/cos) により合計パワーを一定に保ち、
        /// 音量の谷間が生じない滑らかな遷移を実現する。
        /// </summary>
        public void TickCrossfade(float now, float durationSec)
        {
            float elapsed = now - _crossfadeStartedAt;
            float t = Mathf.Clamp01(elapsed / durationSec);

            // 等パワーカーブ: cos^2 + sin^2 = 1 により合計エネルギーが一定
            float angle = t * Mathf.PI * 0.5f;
            float activeGain = Mathf.Cos(angle);
            float standbyGain = Mathf.Sin(angle);

            VideoPlayerManager activeManager = GetActiveManager();
            VideoPlayerManager standbyManager = GetStandbyManager();
            if (activeManager != null) activeManager.SetFadeGain(activeGain);
            if (standbyManager != null) standbyManager.SetFadeGain(standbyGain);
        }

        /// <summary>クロスフェード時間が経過したかを判定する。</summary>
        public bool IsCrossfadeComplete(float now, float durationSec)
        {
            return (now - _crossfadeStartedAt) >= durationSec;
        }

        /// <summary>Inspector で設定されたクロスフェード秒数を返す。</summary>
        public float GetCrossfadeDurationSec()
        {
            return crossfadeDurationSec;
        }

        // =================================================================
        //  切替完了
        // =================================================================

        /// <summary>
        /// ロール交換: 旧 Active 停止、_activeIsA トグル、新 Active の Audio 再配線、AudioLink 切替。
        /// </summary>
        public void CompleteSwitchRoles()
        {
            VideoPlayerManager oldActiveManager = GetActiveManager();
            if (oldActiveManager != null)
            {
                oldActiveManager.Stop();
                oldActiveManager.SetFadeGain(0.0f);
            }

            _activeIsA = !_activeIsA;
            if (_timelineLogging) TL($"a=SWITCH_ROLES");

            VideoPlayerManager newActiveManager = GetActiveManager();
            if (newActiveManager != null)
                newActiveManager.SetFadeGain(1.0f);

            SwitchAudioLinkSource();
        }

        // =================================================================
        //  失敗処理
        // =================================================================

        /// <summary>
        /// Standby 側の接続失敗時に停止する。
        /// Active 側は影響を受けず再生を継続する。
        /// </summary>
        public void StopStandbyOnFailure()
        {
            VideoPlayerManager standbyManager = GetStandbyManager();
            if (standbyManager != null)
            {
                standbyManager.Stop();
                standbyManager.SetFadeGain(0.0f);
            }
        }

        // =================================================================
        //  Active 直接リブート
        // =================================================================

        /// <summary>
        /// 最終手段のリカバリ: 両プレイヤーをリセットし、A で直接再読込する。
        /// ホットスワップが不可能な場合（Standby 接続が繰り返し失敗等）に使用。
        /// </summary>
        public void StartActiveDirectReboot(VRC.SDKBase.VRCUrl url)
        {
            ResetBothPlayersToA();
            if (_timelineLogging) TL($"a=ACTIVE_REBOOT");
            SwitchAudioLinkSource();
            GetActiveManager().LoadURL(url);
        }

        // =================================================================
        //  レンダーテクスチャ
        // =================================================================

        /// <summary>
        /// Active プレイヤーの映像テクスチャをワールドスクリーンへ反映する。
        /// 毎フレーム呼ばれるため、前回と同一テクスチャなら代入をスキップして負荷を抑える。
        /// </summary>
        public void UpdateRenderTexture(int localState, bool ownerPlaying)
        {
            VideoPlayerManager active = GetActiveManager();
            if (active == null) return;

            if (!ownerPlaying && !active.IsPlaying())
            {
                if (_lastAssignedRenderTexture != null)
                {
                    BroadcastVideoTexture(null);
                    _lastAssignedRenderTexture = null;
                }
                return;
            }

            Texture tex = active.GetVideoTexture();
            if (tex == null && verboseLogging)
            {
                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning($"Active texture is null (active={(_activeIsA ? "A" : "B")}, ownerPlaying={ownerPlaying})");
                }
            }
            // 同一テクスチャなら Screen への再代入を省略
            if (tex == _lastAssignedRenderTexture) return;

            BroadcastVideoTexture(tex);

            _lastAssignedRenderTexture = tex;
            if (verboseLogging)
                LogVerbose($"Screen texture updated from active {(_activeIsA ? "A" : "B")}: {(tex != null ? tex.name : "null")}");
        }

        /// <summary>
        /// 指定プレイヤーのテクスチャを強制的にスクリーンへ反映する。
        /// クロスフェード開始時に映像を即座に切替える用途で使用。
        /// </summary>
        private void UpdateRenderTextureFromManager(VideoPlayerManager manager)
        {
            if (manager == null) return;

            Texture tex = manager.GetVideoTexture();
            BroadcastVideoTexture(tex);

            _lastAssignedRenderTexture = tex;
            if (verboseLogging)
                LogVerbose($"Screen texture force-updated from standby: {(tex != null ? tex.name : "null")}");
        }

        /// <summary>登録済みの全 MeshScreen / UiScreen にテクスチャを配信する。</summary>
        private void BroadcastVideoTexture(Texture tex)
        {
            if (meshScreens != null)
            {
                for (int i = 0; i < meshScreens.Length; i++)
                {
                    VideoMeshScreen s = meshScreens[i];
                    if (s != null) s.UpdateVideoTexture(tex);
                }
            }
            if (uiScreens != null)
            {
                for (int i = 0; i < uiScreens.Length; i++)
                {
                    VideoUiScreen s = uiScreens[i];
                    if (s != null) s.UpdateVideoTexture(tex);
                }
            }
        }

        // =================================================================
        //  AudioLink
        // =================================================================

        /// <summary>
        /// audioLinkBehaviour が未設定の場合、シーン内から AudioLink を探索して自動割り当てする。
        /// </summary>
        public void EnsureAudioLinkBehaviourAssignedFromScene()
        {
            if (audioLinkBehaviour != null) return;

            GameObject audioLinkObject = GameObject.Find("AudioLink");
            if (audioLinkObject == null)
            {
                LogWarning("AudioLink behaviour not found in scene (GameObject: AudioLink)");
                return;
            }

            UdonSharpBehaviour[] behaviours = audioLinkObject.GetComponents<UdonSharpBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                UdonSharpBehaviour candidate = behaviours[i];
                if (candidate == null || candidate == this) continue;
                if (candidate.GetType().Name != "AudioLink") continue;

                audioLinkBehaviour = candidate;
                LogMessage($"AudioLink behaviour auto-assigned: {candidate.name}");
                return;
            }

            LogWarning("AudioLink behaviour not found on GameObject: AudioLink");
        }

        /// <summary>
        /// AudioLink のソースを現在の Active プレイヤーの AudioSource に切替える。
        /// ビジュアライザが常に再生中の音声を参照するようにするため。
        /// </summary>
        public void SwitchAudioLinkSource()
        {
            if (audioLinkBehaviour == null)
                EnsureAudioLinkBehaviourAssignedFromScene();

            if (audioLinkBehaviour == null)
            {
                LogVerbose("SwitchAudioLinkSource skipped: audioLinkBehaviour is null");
                return;
            }
            AudioSilenceDetector activeDetector = GetActiveSilenceDetector();
            if (activeDetector == null)
            {
                LogWarning("SwitchAudioLinkSource failed: active detector is null");
                return;
            }
            // SilenceDetector と AudioSource は同一 GameObject に配置される前提
            AudioSource source = activeDetector.GetComponent<AudioSource>();
            if (source != null)
            {
                audioLinkBehaviour.SetProgramVariable("audioSource", source);
                LogVerbose($"AudioLink source switched: {source.name}");
            }
            else
            {
                LogWarning("SwitchAudioLinkSource failed: active AudioSource is null");
            }
        }

        // =================================================================
        //  ログ
        // =================================================================

        /// <summary>通常ログ出力。</summary>
        private void LogMessage(string message)
        {
            Debug.Log($"[AunCast/Switcher] {message}", this);
        }

        /// <summary>詳細モード時のみ出力されるログ。</summary>
        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            LogMessage(message);
        }

        /// <summary>警告レベルのログ出力。</summary>
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/Switcher] {message}", this);
        }

        /// <summary>タイムラインログ: サーバー時刻付きで状態遷移を記録する。</summary>
        private void TL(string eventAndData)
        {
            Debug.Log($"[AunCast:TL] st={VRC.SDKBase.Networking.GetServerTimeInMilliseconds()} c=PBS {eventAndData}");
        }
    }
}
