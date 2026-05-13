
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Video.Components.AVPro;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// AVPro ラッパー。VRCAVProVideoPlayer のイベントを LocalDualPlayerController に転送する。
    /// Active 用と Standby 用の 2 インスタンスを配置し、playerIndex で識別する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class VideoPlayerManager : UdonSharpBehaviour
    {
        /// <summary>映像イベントの転送先 FSM。再生状態の管理はすべてこちらに委譲する。</summary>
        [Tooltip("イベント転送先の LocalDualPlayerController")]
        public LocalDualPlayerController receiver;

        /// <summary>コールバック時にどちらのプレイヤーからの通知かを識別するためのインデックス (0=A, 1=B)。</summary>
        [Tooltip("このマネージャーのプレイヤー識別子（0 = A, 1 = B）")]
        public int playerIndex;

        /// <summary>ラップ対象の VRChat AVPro プレイヤー本体。</summary>
        public VRCAVProVideoPlayer avProPlayer;

        /// <summary>デコード済み映像テクスチャを保持する Renderer。シェーダーによりプロパティ名が異なる。</summary>
        public Renderer avProTextureRenderer;

        /// <summary>このプレイヤーに紐づく全 AudioSource。音量・クロスフェードの反映対象。</summary>
        public AudioSource[] audioSources;

        [Header("Debug")]
        [Tooltip("VideoPlayerManager の詳細ログを出力する")]
        public bool verboseLogging = true;

        private Material avproFetchMaterial;
        private Texture _lastLoggedTexture;
        private float _lastNullTextureWarnAt;

        /// <summary>AudioSource ごとの初期音量。UI 音量・クロスフェードの最終出力に乗算して使う。</summary>
        private float[] _audioSourceBaseVolumes;
        private AudioSource[] _cachedAudioSourcesForBaseVolume;

        private bool _initialized;

        /// <summary>テクスチャ取得用マテリアル参照を事前にキャッシュする。</summary>
        public void Start()
        {
            if (_initialized)
                return;

            CacheAudioSourceBaseVolumes();
            EnsureFetchMaterial();
            LogVerbose($"Initialized (playerIndex={playerIndex}, avPro={(avProPlayer != null)}, renderer={(avProTextureRenderer != null)})");

            _initialized = true;
        }

        // --- VRChat コールバック群 ---
        // playerIndex を付与して controller に転送することで、
        // どちらのプレイヤーで発生したイベントかを FSM 側で判別できるようにする。

        public override void OnVideoEnd()
        {
            if (receiver == null) return;
            LogVerbose("OnVideoEnd");
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoEnd();
        }

        public override void OnVideoError(VRC.SDK3.Components.Video.VideoError videoError)
        {
            if (receiver == null) return;
            LogWarning($"OnVideoError: {videoError}");
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver._lastVideoError = videoError;
            receiver.OnManagerVideoError();
        }

        public override void OnVideoLoop()
        {
            if (receiver == null) return;
            LogVerbose("OnVideoLoop");
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoLoop();
        }

        public override void OnVideoReady()
        {
            if (receiver == null) return;
            LogVerbose("OnVideoReady");
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoReady();
        }

        public override void OnVideoStart()
        {
            if (receiver == null) return;
            LogVerbose("OnVideoStart");
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoStart();
        }

        // --- avProPlayer への薄いラッパー ---
        // コントローラーが VRCAVProVideoPlayer を直接触らず、
        // ログ出力やプレイヤー切替を一元管理するための委譲先。

        public void Play()
        {
            avProPlayer.Play();
            LogVerbose("Play");
        }

        public void Pause()
        {
            avProPlayer.Pause();
            LogVerbose("Pause");
        }

        public void Stop()
        {
            avProPlayer.Stop();
            LogVerbose("Stop");
        }

        public float GetTime() => avProPlayer.GetTime();
        public bool IsPlaying() => avProPlayer.IsPlaying;
        public void LoadURL(VRCUrl url)
        {
            avProPlayer.LoadURL(url);
            LogVerbose($"LoadURL: {(url != null ? url.Get() : "null")}");
        }

        /// <summary>
        /// デコード済みのビデオテクスチャを取得する。
        /// AVPro はシェーダーやプラットフォームによりテクスチャのプロパティ名が異なるため、
        /// 複数の取得経路とプロパティ名を順に試行する。
        /// </summary>
        public Texture GetVideoTexture()
        {
            // 取得経路の違い（material / sharedMaterial）を順に吸収する
            Texture tex;

            Material mat = EnsureFetchMaterial();
            tex = GetTextureByKnownParams(mat);
            if (tex != null) return ReportTexture(tex, "material");

            if (avProTextureRenderer == null)
                return null;

            mat = avProTextureRenderer.sharedMaterial;
            tex = GetTextureByKnownParams(mat);
            if (tex != null) return ReportTexture(tex, "sharedMaterial");

            Material[] mats = avProTextureRenderer.materials;
            if (mats != null && mats.Length > 0)
            {
                tex = GetTextureByKnownParams(mats[0]);
                if (tex != null)
                {
                    avproFetchMaterial = mats[0];
                    return ReportTexture(tex, "materials[0]");
                }
            }

            Material[] sharedMats = avProTextureRenderer.sharedMaterials;
            if (sharedMats != null && sharedMats.Length > 0)
            {
                tex = GetTextureByKnownParams(sharedMats[0]);
                if (tex != null)
                {
                    avproFetchMaterial = sharedMats[0];
                    return ReportTexture(tex, "sharedMaterials[0]");
                }
            }

            if (avProPlayer.IsPlaying)
            {
                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning("GetVideoTexture returned null");
                }
            }
            return null;
        }

        /// <summary>ユーザーが設定するマスター音量 (0-1)。</summary>
        private float _currentVolume = 1f;

        /// <summary>クロスフェード時に外部から適用される乗算ゲイン (0-1)。</summary>
        private float _fadeGain = 1f;

        /// <summary>ユーザー設定のマスター音量を返す。</summary>
        public float GetVolume() => _currentVolume;

        /// <summary>ユーザー設定のマスター音量を変更し、AudioSource に反映する。</summary>
        public void SetVolume(float volume)
        {
            _currentVolume = Mathf.Clamp01(volume);
            ApplyVolume();
        }

        /// <summary>クロスフェード用のゲイン（0.0〜1.0）。AudioSource.volume に乗算される。</summary>
        public void SetFadeGain(float fadeGain)
        {
            _fadeGain = Mathf.Clamp01(fadeGain);
            ApplyVolume();
        }

        /// <summary>現在のクロスフェードゲインを返す。</summary>
        public float GetFadeGain() => _fadeGain;

        /// <summary>
        /// volume と fadeGain を合成して全 AudioSource に適用する。
        /// 知覚リニアな音量変化のため、x^2 と指数カーブのブレンドを使用する。
        /// </summary>
        private void ApplyVolume()
        {
            if (audioSources == null) return;

            // 左側は x^2 ベース、右側は Dr. Lex 指数カーブ (50dB レンジ) を補間係数 x で lerp。
            // これで指数カーブ単体だと発生する左半分の「死にゾーン」を避けつつ、
            // 右端付近は知覚的にリニアな音量上昇を維持する。
            // 指数カーブの参考: https://www.dr-lex.be/info-stuff/volumecontrols.html#ideal
            float x = _currentVolume;
            float expCurve = Mathf.Clamp01(3.1623e-3f * Mathf.Exp(x * 5.757f) - 3.1623e-3f);
            float adjustedVolume = (1f - x) * x * x + x * expCurve;
            float output = adjustedVolume * _fadeGain;

            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource audioSource = audioSources[i];
                if (audioSource == null) continue;
                float baseVolume = 1f;
                if (_audioSourceBaseVolumes != null && i < _audioSourceBaseVolumes.Length)
                    baseVolume = _audioSourceBaseVolumes[i];

                audioSource.volume = Mathf.Clamp01(baseVolume * output);
            }
        }

        private void CacheAudioSourceBaseVolumes()
        {
            if (audioSources == null)
            {
                _audioSourceBaseVolumes = null;
                _cachedAudioSourcesForBaseVolume = null;
                return;
            }

            bool needsResize = _audioSourceBaseVolumes == null
                || _cachedAudioSourcesForBaseVolume == null
                || _audioSourceBaseVolumes.Length != audioSources.Length
                || _cachedAudioSourcesForBaseVolume.Length != audioSources.Length;

            if (needsResize)
            {
                _audioSourceBaseVolumes = new float[audioSources.Length];
                _cachedAudioSourcesForBaseVolume = new AudioSource[audioSources.Length];
            }

            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource source = audioSources[i];
                if (!needsResize && _cachedAudioSourcesForBaseVolume[i] == source)
                    continue;

                _cachedAudioSourcesForBaseVolume[i] = source;
                _audioSourceBaseVolumes[i] = source != null ? source.volume : 1f;
            }
        }

        /// <summary>テクスチャ取得用マテリアルをキャッシュし、毎フレームのインスタンス生成を防ぐ。</summary>
        private Material EnsureFetchMaterial()
        {
            if (avproFetchMaterial != null)
                return avproFetchMaterial;
            if (avProTextureRenderer == null)
                return null;

            avproFetchMaterial = avProTextureRenderer.material;
            return avproFetchMaterial;
        }

        /// <summary>
        /// AVPro が映像を書き込むシェーダープロパティはシェーダーごとに異なるため、
        /// 既知のプロパティ名を順に探索してテクスチャを返す。
        /// </summary>
        private Texture GetTextureByKnownParams(Material mat)
        {
            if (mat == null) return null;

            // 環境差分を吸収するため複数プロパティを順に確認する
            if (mat.HasProperty("_MainTex"))
            {
                Texture tex = mat.GetTexture("_MainTex");
                if (tex != null) return tex;
            }
            if (mat.HasProperty("_EmissionMap"))
            {
                Texture tex = mat.GetTexture("_EmissionMap");
                if (tex != null) return tex;
            }
            if (mat.HasProperty("_BaseMap"))
            {
                Texture tex = mat.GetTexture("_BaseMap");
                if (tex != null) return tex;
            }
            if (mat.HasProperty("_BaseColorMap"))
            {
                Texture tex = mat.GetTexture("_BaseColorMap");
                if (tex != null) return tex;
            }

            return null;
        }

        private Texture ReportTexture(Texture texture, string source)
        {
            if (texture != _lastLoggedTexture)
            {
                _lastLoggedTexture = texture;
                LogVerbose($"Video texture updated via {source}: {texture.name}");
            }
            return texture;
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            Debug.Log($"[AunCast/VideoPlayerManager[{playerIndex}]] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/VideoPlayerManager[{playerIndex}]] {message}", this);
        }

    }
}
