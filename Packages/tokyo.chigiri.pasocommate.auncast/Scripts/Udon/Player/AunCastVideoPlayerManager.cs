
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Video.Components.AVPro;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// AVPro ラッパー。VRCAVProVideoPlayer のイベントを AunCastDualPlayerController に転送する。
    /// Active 用と Standby 用の 2 インスタンスを配置し、playerIndex で識別する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastVideoPlayerManager : UdonSharpBehaviour
    {
        /// <summary>映像イベントの転送先 FSM。再生状態の管理はすべてこちらに委譲する。</summary>
        [Tooltip("イベント転送先の AunCastDualPlayerController")]
        public AunCastDualPlayerController receiver;

        /// <summary>コールバック時にどちらのプレイヤーからの通知かを識別するためのインデックス (0=A, 1=B)。</summary>
        [Tooltip("このマネージャーのプレイヤー識別子（0 = A, 1 = B）")]
        public int playerIndex;

        /// <summary>ラップ対象の VRChat AVPro プレイヤー本体。</summary>
        public VRCAVProVideoPlayer avProPlayer;

        /// <summary>デコード済み映像テクスチャを保持する Renderer。シェーダーによりプロパティ名が異なる。</summary>
        public Renderer avProTextureRenderer;

        /// <summary>このプレイヤーに紐づく全 AudioSource。音量・クロスフェードの反映対象。</summary>
        public AudioSource[] audioSources;

        private Material avproFetchMaterial;
        private float _lastNullTextureWarnAt;

        /// <summary>AudioSource ごとの基準音量。AunCastSpeaker.baseVolume を優先し、UI 音量・クロスフェードの最終出力に乗算して使う。</summary>
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

            _initialized = true;
        }

        // --- VRChat コールバック群 ---
        // playerIndex を付与して controller に転送することで、
        // どちらのプレイヤーで発生したイベントかを FSM 側で判別できるようにする。

        public override void OnVideoEnd()
        {
            if (receiver == null) return;
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
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoLoop();
        }

        public override void OnVideoReady()
        {
            if (receiver == null) return;
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoReady();
        }

        public override void OnVideoStart()
        {
            if (receiver == null) return;
            receiver._lastCallbackPlayerIndex = playerIndex;
            receiver.OnManagerVideoStart();
        }

        // --- avProPlayer への薄いラッパー ---
        // コントローラーが VRCAVProVideoPlayer を直接触らず、
        // ログ出力やプレイヤー切替を一元管理するための委譲先。

        public void Play()
        {
            avProPlayer.Play();
        }

        public void Pause()
        {
            avProPlayer.Pause();
        }

        public void Stop()
        {
            avProPlayer.Stop();
        }

        public float GetTime() => avProPlayer.GetTime();
        public bool IsPlaying() => avProPlayer.IsPlaying;
        public void LoadURL(VRCUrl url)
        {
            avProPlayer.LoadURL(url);
        }

        /// <summary>
        /// デコード済みのビデオテクスチャを取得する。
        /// AVPro はシェーダーやプラットフォームによりテクスチャのプロパティ名が異なるため、
        /// 複数の取得経路とプロパティ名を順に試行する。
        /// </summary>
        public Texture GetVideoTexture()
        {
            return GetVideoTextureInternal(true);
        }

        /// <summary>
        /// AVPro が Grab マテリアルへ設定した _MainTex_ST から、映像の Y 反転状態を取得する。
        /// </summary>
        public bool GetVideoFlipY()
        {
            Material mat = EnsureFetchMaterial();
            if (mat == null) return false;
            if (!mat.HasProperty("_MainTex")) return false;

            return mat.GetTextureScale("_MainTex").y < 0f;
        }

        /// <summary>
        /// 音声のみフォールバック中に、null 警告を出さず映像テクスチャの復帰だけを確認する。
        /// </summary>
        public Texture GetVideoTextureSilent()
        {
            return GetVideoTextureInternal(false);
        }

        private Texture GetVideoTextureInternal(bool warnOnNull)
        {
            // 取得経路の違い（material / sharedMaterial）を順に吸収する
            Texture tex;

            Material mat = EnsureFetchMaterial();
            tex = GetTextureByKnownParams(mat);
            if (tex != null)
            {
                avproFetchMaterial = mat;
                return tex;
            }

            if (avProTextureRenderer == null)
                return null;

            mat = avProTextureRenderer.sharedMaterial;
            tex = GetTextureByKnownParams(mat);
            if (tex != null)
            {
                avproFetchMaterial = mat;
                return tex;
            }

            Material[] mats = avProTextureRenderer.materials;
            if (mats != null && mats.Length > 0)
            {
                tex = GetTextureByKnownParams(mats[0]);
                if (tex != null)
                {
                    avproFetchMaterial = mats[0];
                    return tex;
                }
            }

            Material[] sharedMats = avProTextureRenderer.sharedMaterials;
            if (sharedMats != null && sharedMats.Length > 0)
            {
                tex = GetTextureByKnownParams(sharedMats[0]);
                if (tex != null)
                {
                    avproFetchMaterial = sharedMats[0];
                    return tex;
                }
            }

            if (warnOnNull && avProPlayer.IsPlaying)
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

        /// <summary>音量カーブとクロスフェードを反映した出力ゲインを返す。</summary>
        public float GetCurrentOutputGain()
        {
            return GetAdjustedVolume(_currentVolume) * _fadeGain;
        }

        /// <summary>指定 AudioSource に実際に適用される出力ゲインを返す。</summary>
        public float GetAppliedOutputGain(AudioSource target)
        {
            float baseVolume = 1f;
            if (target != null && audioSources != null)
            {
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] != target) continue;
                    if (_audioSourceBaseVolumes != null && i < _audioSourceBaseVolumes.Length)
                        baseVolume = _audioSourceBaseVolumes[i];
                    break;
                }
            }
            return Mathf.Clamp01(baseVolume * GetCurrentOutputGain());
        }

        /// <summary>
        /// volume と fadeGain を合成して全 AudioSource に適用する。
        /// 知覚リニアな音量変化のため、x^2 と指数カーブのブレンドを使用する。
        /// </summary>
        private void ApplyVolume()
        {
            if (audioSources == null) return;

            float output = GetCurrentOutputGain();

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

        private float GetAdjustedVolume(float volume)
        {
            // スライダー値 0 はミュート扱い。それ以外は -34 dBFS (=0.02) より下の
            // 死にゾーンを除去するため x∈(0,1] を t∈[0.15,1] にリマップしてから
            // 既存の Dr. Lex 指数カーブ (50dB レンジ) を適用する。
            // 指数カーブの参考: https://www.dr-lex.be/info-stuff/volumecontrols.html#ideal
            float x = Mathf.Clamp01(volume);
            if (x <= 0f) return 0f;

            float t = 0.15f + 0.85f * x;
            float expCurve = Mathf.Clamp01(3.1623e-3f * Mathf.Exp(t * 5.757f) - 3.1623e-3f);
            return (1f - t) * t * t + t * expCurve;
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
                if (source == null)
                {
                    _audioSourceBaseVolumes[i] = 1f;
                    continue;
                }

                AunCastSpeaker speaker = source.GetComponent<AunCastSpeaker>();
                _audioSourceBaseVolumes[i] = speaker != null ? speaker.GetBaseVolume() : source.volume;
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

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/AunCastVideoPlayerManager[{playerIndex}]] {message}", this);
        }

    }
}
