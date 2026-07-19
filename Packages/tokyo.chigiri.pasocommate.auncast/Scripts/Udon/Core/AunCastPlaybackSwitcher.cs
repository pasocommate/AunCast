
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// Active/Standby の PlayerManager 切替とクロスフェードを担当するコンポーネント。
    /// AunCastDualPlayerController と同一 GameObject に配置される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastPlaybackSwitcher : UdonSharpBehaviour
    {
        // =================================================================
        //  Inspector 参照
        // =================================================================
        [SerializeField] private AunCastVideoPlayerManager playerManagerA;
        [SerializeField] private AunCastVideoPlayerManager playerManagerB;
        [SerializeField] private AunCastSpeaker silenceDetectorA;
        [SerializeField] private AunCastSpeaker silenceDetectorB;
        [SerializeField] private AunCastEventBus eventBus;
        [SerializeField] private UdonSharpBehaviour audioLinkBehaviour;

        // =================================================================
        //  Inspector パラメータ
        // =================================================================
        [Header("Crossfade")]
        [Tooltip("クロスフェード時間（秒）")]
        [SerializeField] private float crossfadeDurationSec = 0.1f;

        [Header("Audio Only Fallback")]
        [Tooltip("映像テクスチャが取得できなくても、再生時刻が前進している場合は音声のみ配信として切替を完了する")]
        [SerializeField] private bool allowAudioOnlyFallback = true;
        [Tooltip("音声のみ配信として扱うまで、Standby の映像テクスチャ到着を待つ時間（秒）")]
        [SerializeField] private float audioOnlyFallbackDelaySec = 3.0f;
        [Tooltip("音声のみ配信として扱うために必要な Standby の再生時刻前進量（秒）")]
        [SerializeField] private float audioOnlyFallbackMinAdvanceSec = 0.5f;

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
        private bool _lastAssignedVideoFlipY;

        /// <summary>null テクスチャ警告のスロットル用タイムスタンプ</summary>
        private float _lastNullTextureWarnAt;
        private float _lastSwitchTextureWaitWarnAt;
        private float _switchTextureWaitStartedAt;
        private float _audioOnlyFallbackBaseTime;

        private bool _crossfading;
        private bool _switchTextureDisplayed;
        private bool _switchingAudioOnlyFallback;
        private bool _activeAudioOnlyFallback;

        // =================================================================
        //  Active/Standby 取得
        // =================================================================

        /// <summary>PlayerManager A への直接参照（初期化・デバッグ用）。</summary>
        public AunCastVideoPlayerManager GetPlayerManagerA() => playerManagerA;
        /// <summary>PlayerManager B への直接参照（初期化・デバッグ用）。</summary>
        public AunCastVideoPlayerManager GetPlayerManagerB() => playerManagerB;

        /// <summary>現在視聴者に出力中のプレイヤーを返す。</summary>
        public AunCastVideoPlayerManager GetActiveManager()
        {
            return _activeIsA ? playerManagerA : playerManagerB;
        }

        /// <summary>次回切替用に待機中のプレイヤーを返す。</summary>
        public AunCastVideoPlayerManager GetStandbyManager()
        {
            return _activeIsA ? playerManagerB : playerManagerA;
        }

        /// <summary>Active 側に対応する無音検知器を返す。</summary>
        public AunCastSpeaker GetActiveSilenceDetector()
        {
            return _activeIsA ? silenceDetectorA : silenceDetectorB;
        }

        /// <summary>Standby 側に対応する無音検知器を返す。</summary>
        public AunCastSpeaker GetStandbySilenceDetector()
        {
            return _activeIsA ? silenceDetectorB : silenceDetectorA;
        }

        /// <summary>外部から Active 判定を参照するためのアクセサ。</summary>
        public bool GetActiveIsA()
        {
            return _activeIsA;
        }

        /// <summary>
        /// 初期状態を確立する: A をフル音量で Active、B をミュートで Standby に設定。
        /// </summary>
        public void InitializeToA()
        {
            _activeIsA = true;
            _activeAudioOnlyFallback = false;
            _switchingAudioOnlyFallback = false;
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
            _activeAudioOnlyFallback = false;
            _switchingAudioOnlyFallback = false;
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
            AunCastVideoPlayerManager standbyManager = GetStandbyManager();
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
        /// クロスフェードを開始する。Standby 側の映像テクスチャ取得後に表示を切替え、
        /// 音声を徐々にフェードさせることで視聴者の違和感を最小化する。
        /// </summary>
        public void StartCrossfade(float now)
        {
            _crossfadeStartedAt = now;
            _crossfading = true;
            _switchTextureDisplayed = false;
            _switchingAudioOnlyFallback = false;
            _switchTextureWaitStartedAt = now;
            AunCastVideoPlayerManager standby = GetStandbyManager();
            _audioOnlyFallbackBaseTime = standby != null ? standby.GetTime() : 0f;

            // Standby の実テクスチャが取れない間は旧映像を保持し、白フレームを出さない。
            TryEnsureSwitchTextureDisplayed(now);

            // ゲインを初期値にリセットしてから TickCrossfade で漸次変化させる
            SetRolesGain(1.0f, 0.0f);

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
            if (!TryEnsureSwitchTextureDisplayed(now))
            {
                // Standby テクスチャ未取得中は Active ゲインを維持して映像を保持する
                SetRolesGain(1.0f, 0.0f);
                return;
            }

            if (durationSec <= 0f)
            {
                SetRolesGain(0.0f, 1.0f);
                return;
            }

            float elapsed = now - _crossfadeStartedAt;
            float t = Mathf.Clamp01(elapsed / durationSec);
            // 等パワーカーブ: cos^2 + sin^2 = 1 により合計エネルギーが一定
            float angle = t * Mathf.PI * 0.5f;
            SetRolesGain(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>Active / Standby 両プレイヤーのフェードゲインをまとめて設定する。</summary>
        private void SetRolesGain(float activeGain, float standbyGain)
        {
            AunCastVideoPlayerManager active = GetActiveManager();
            AunCastVideoPlayerManager standby = GetStandbyManager();
            if (active != null) active.SetFadeGain(activeGain);
            if (standby != null) standby.SetFadeGain(standbyGain);
        }

        /// <summary>クロスフェード時間が経過したかを判定する。</summary>
        public bool IsCrossfadeComplete(float now, float durationSec)
        {
            if (!_switchTextureDisplayed) return false;
            if (durationSec <= 0f) return true;
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
            AunCastVideoPlayerManager oldActiveManager = GetActiveManager();
            if (oldActiveManager != null)
            {
                oldActiveManager.Stop();
                oldActiveManager.SetFadeGain(0.0f);
            }

            _activeIsA = !_activeIsA;
            _crossfading = false;
            if (_timelineLogging) TL($"a=SWITCH_ROLES");

            AunCastVideoPlayerManager newActiveManager = GetActiveManager();
            if (newActiveManager != null)
                newActiveManager.SetFadeGain(1.0f);

            _activeAudioOnlyFallback = _switchingAudioOnlyFallback;
            _switchingAudioOnlyFallback = false;

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
            // Crossfade 中の中断（サイクルタイムアウト、URL 変更、Reboot）では
            // _crossfading と Active の途中ゲインが残り、映像更新の停止と音量低下が
            // 固着する。CompleteSwitchRoles を通らない経路なのでここで復帰させる。
            if (_crossfading)
            {
                _crossfading = false;
                SetRolesGain(1.0f, 0.0f);
            }

            AunCastVideoPlayerManager standbyManager = GetStandbyManager();
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
        /// テクスチャが未到着で再試行が必要な場合だけ true を返す。
        /// </summary>
        public bool UpdateRenderTexture(int localState, bool ownerPlaying)
        {
            // Crossfade 中の映像切替は TickCrossfade 内で処理する。
            if (_crossfading) return false;

            AunCastVideoPlayerManager active = GetActiveManager();
            if (active == null) return false;

            if (!ownerPlaying && !active.IsPlaying())
            {
                if (_lastAssignedRenderTexture != null)
                {
                    BroadcastVideoTexture(null, false);
                    _lastAssignedRenderTexture = null;
                    _lastAssignedVideoFlipY = false;
                }
                return false;
            }

            Texture tex = _activeAudioOnlyFallback
                ? active.GetVideoTextureSilent()
                : active.GetVideoTexture();
            if (tex == null)
            {
                if (_activeAudioOnlyFallback)
                {
                    if (_lastAssignedRenderTexture != null)
                    {
                        BroadcastVideoTexture(null, false);
                        _lastAssignedRenderTexture = null;
                        _lastAssignedVideoFlipY = false;
                    }
                    return false;
                }

                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning($"Active texture is null (active={(_activeIsA ? "A" : "B")}, ownerPlaying={ownerPlaying})");
                }
                // 再生開始コールバックよりテクスチャ取得が遅れる場合だけ再試行する。
                return true;
            }

            bool flipY = active.GetVideoFlipY();
            // 同一テクスチャかつ同一反転なら Screen への再代入を省略
            if (tex == _lastAssignedRenderTexture && flipY == _lastAssignedVideoFlipY) return false;

            BroadcastVideoTexture(tex, flipY);

            _lastAssignedRenderTexture = tex;
            _lastAssignedVideoFlipY = flipY;
            _activeAudioOnlyFallback = false;
            return false;
        }

        /// <summary>
        /// 指定プレイヤーのテクスチャを強制的にスクリーンへ反映する。
        /// クロスフェード開始時に、取得済みの Standby 映像だけを表示へ出す用途で使用。
        /// </summary>
        private bool TryUpdateRenderTextureFromManager(AunCastVideoPlayerManager manager)
        {
            if (manager == null) return false;

            Texture tex = manager.GetVideoTexture();
            if (tex == null)
            {
                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning("Switch texture is null; keeping previous video texture");
                }
                return false;
            }

            bool flipY = manager.GetVideoFlipY();
            if (tex != _lastAssignedRenderTexture || flipY != _lastAssignedVideoFlipY)
                BroadcastVideoTexture(tex, flipY);

            _lastAssignedRenderTexture = tex;
            _lastAssignedVideoFlipY = flipY;
            return true;
        }

        private bool TryEnsureSwitchTextureDisplayed(float now)
        {
            if (_switchTextureDisplayed) return true;

            _switchTextureDisplayed = TryUpdateRenderTextureFromManager(GetStandbyManager());
            if (_switchTextureDisplayed)
            {
                _crossfadeStartedAt = now;
                return true;
            }

            if (TryAcceptAudioOnlyFallback(now))
                return true;

            if (now - _lastSwitchTextureWaitWarnAt > 2.0f)
            {
                _lastSwitchTextureWaitWarnAt = now;
                LogWarning("Waiting for standby texture before completing switch");
            }
            return false;
        }

        /// <summary>
        /// 映像テクスチャが来ないが Standby の時間が前進している場合、音声のみ配信として切替を許可する。
        /// </summary>
        private bool TryAcceptAudioOnlyFallback(float now)
        {
            if (!allowAudioOnlyFallback) return false;

            AunCastVideoPlayerManager standby = GetStandbyManager();
            if (standby == null || !standby.IsPlaying()) return false;

            float elapsed = now - _switchTextureWaitStartedAt;
            if (elapsed < Mathf.Max(0f, audioOnlyFallbackDelaySec)) return false;

            float timeAdvance = standby.GetTime() - _audioOnlyFallbackBaseTime;
            if (timeAdvance < Mathf.Max(0f, audioOnlyFallbackMinAdvanceSec)) return false;

            _switchTextureDisplayed = true;
            _switchingAudioOnlyFallback = true;
            _crossfadeStartedAt = now;

            if (_lastAssignedRenderTexture != null)
            {
                BroadcastVideoTexture(null, false);
                _lastAssignedRenderTexture = null;
                _lastAssignedVideoFlipY = false;
            }

            TL($"a=AUDIO_ONLY_FALLBACK adv={timeAdvance:F2} wait={elapsed:F2}");
            LogMessage($"Audio-only fallback accepted (advance={timeAdvance:F2}s, wait={elapsed:F2}s)");
            return true;
        }

        /// <summary>停止時にスクリーン購読者へ idle 表示を即時配信する。</summary>
        public void ClearVideoTexture()
        {
            BroadcastVideoTexture(null, false);
            _lastAssignedRenderTexture = null;
            _lastAssignedVideoFlipY = false;
            LogMessage("Video texture cleared to idle");
        }

        /// <summary>AunCastEventBus 経由で全スクリーン購読者へテクスチャを配信する。</summary>
        private void BroadcastVideoTexture(Texture tex, bool flipY)
        {
            if (eventBus != null)
                eventBus.PublishVideoTexture(tex, tex != null && flipY);
        }

        /// <summary>タイムラインログをローカルのみ設定する。</summary>
        public void SetTimelineLoggingLocal(bool value)
        {
            _timelineLogging = value;
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
                return;
            }
            AunCastSpeaker activeDetector = GetActiveSilenceDetector();
            if (activeDetector == null)
            {
                LogWarning("SwitchAudioLinkSource failed: active detector is null");
                return;
            }
            // SilenceDetector と AudioSource は同一 GameObject に配置される前提
            AudioSource source = activeDetector.GetAudioSource();
            if (source != null)
            {
                audioLinkBehaviour.SetProgramVariable("audioSource", source);
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
