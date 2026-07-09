
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// AunCast の音声出力として使う AudioSource を宣言し、AudioSource.GetOutputData() で
    /// メインスレッドから PCM を取得して RMS 無音検知も行うコンポーネント。
    ///
    /// 注意: OnAudioFilterRead 内のフィールド書き込みは Udon VM ヒープと分離されており
    /// メインスレッドから読めないため、GetOutputData を使用する（VRChat-Udon-Development-Notes 9.6 参照）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastSpeaker : UdonSharpBehaviour
    {
        public const int PLAYER_A = 0;
        public const int PLAYER_B = 1;

        public const int MODE_STEREO = 0;
        public const int MODE_LEFT = 1;
        public const int MODE_RIGHT = 2;

        [Header("AunCast Speaker")]
        [Tooltip("このスピーカーを接続するプレイヤー系統（0 = PlayerA, 1 = PlayerB）")]
        public int playerIndex = PLAYER_A;

        [Tooltip("VRCAVProVideoSpeaker.mode 相当（0 = Stereo, 1 = Left, 2 = Right）")]
        public int mode = MODE_STEREO;

        [Tooltip("AunCast が音量計算に使う基準音量。AudioSource.volume はランタイム出力値として上書きされる。")]
        [Range(0f, 1f)]
        public float baseVolume = 1f;

        private float _adjustedUserVolume = 1f;
        private float _fadeGain = 1f;

        [Header("Silence Detection")]
        [Tooltip("無音判定 RMS 閾値 (dBFS)")]
        [SerializeField] private float silenceRmsThresholdDbfs = -60f;

        [Tooltip("無音判定の継続時間（秒）")]
        [SerializeField] private float silenceConsecutiveSec = 2.0f;

        // --- RMS 計測（メインスレッドで GetOutputData を使用） ---

        /// <summary>同一 GameObject 上の AudioSource。Start で取得しキャッシュする。</summary>
        private AudioSource _audioSource;

        /// <summary>GetOutputData の受け皿。GC 回避のため使い回す。</summary>
        private float[] _outputBuffer;
        private const int OUTPUT_BUFFER_SAMPLES = 1024;
        private const float MIN_DBFS = -96f;
        private const float DBFS_EPSILON = 0.000001f;
        /// <summary>この音量以下は無音確定として GetOutputData を省略する。</summary>
        private const float VOLUME_EPSILON = 0.0001f;

        /// <summary>直近の RMS 値。同一フレーム内の再計算を避けるキャッシュ。</summary>
        private float _lastRms;
        private int _lastRmsFrame = -1;

        private void Start()
        {
            _audioSource = GetComponent<AudioSource>();
            _outputBuffer = new float[OUTPUT_BUFFER_SAMPLES];
        }

        /// <summary>AudioSource.GetOutputData で現在の出力 PCM から RMS を計算する。メインスレッド専用。</summary>
        public float GetRms()
        {
            if (_audioSource == null || _outputBuffer == null) { _lastRms = -1f; return 0f; }
            if (_lastRmsFrame == Time.frameCount) return _lastRms;

            // volume 0 のときは GetOutputData も全ゼロを返すと分かり切っているため、
            // 呼び出し自体を省略して負荷を減らす。
            if (_audioSource.volume <= VOLUME_EPSILON)
            {
                _lastRms = 0f;
                _lastRmsFrame = Time.frameCount;
                return 0f;
            }

            _audioSource.GetOutputData(_outputBuffer, 0);

            float acc = 0f;
            for (int i = 0; i < _outputBuffer.Length; i++)
                acc += _outputBuffer[i] * _outputBuffer[i];

            float rms = Mathf.Sqrt(acc / _outputBuffer.Length);
            _lastRms = rms;
            _lastRmsFrame = Time.frameCount;
            return rms;
        }

        // --- 音量制御 ---

        public void SetBaseVolume(float volume)
        {
            baseVolume = Mathf.Clamp01(volume);
            ApplyVolume();
        }

        public void SetAdjustedUserVolume(float adjustedVolume)
        {
            _adjustedUserVolume = Mathf.Clamp01(adjustedVolume);
            ApplyVolume();
        }

        public void SetFadeGain(float fadeGain)
        {
            _fadeGain = Mathf.Clamp01(fadeGain);
            ApplyVolume();
        }

        private void ApplyVolume()
        {
            if (_audioSource == null) return;
            _audioSource.volume = Mathf.Clamp01(baseVolume * _adjustedUserVolume * _fadeGain);
        }

        // --- Getters ---

        /// <summary>無音と判定するのに必要な連続秒数。自動リシンク判定で使用。</summary>
        public float GetSilenceConsecutiveSec() { return silenceConsecutiveSec; }

        /// <summary>無音と見なす RMS 閾値（リニア）。dBFS から変換して返す。</summary>
        public float GetSilenceRmsThreshold() { return Mathf.Pow(10f, silenceRmsThresholdDbfs / 20f); }
        public float GetSilenceRmsThresholdDbfs() { return Mathf.Clamp(silenceRmsThresholdDbfs, MIN_DBFS, 0f); }
        public int GetPlayerIndex() { return playerIndex == PLAYER_B ? PLAYER_B : PLAYER_A; }
        public int GetMode() { return Mathf.Clamp(mode, MODE_STEREO, MODE_RIGHT); }
        public AudioSource GetAudioSource() { return _audioSource != null ? _audioSource : GetComponent<AudioSource>(); }

        public float GetLastRmsDbfs()
        {
            float rms = GetRms();
            if (rms <= DBFS_EPSILON) return MIN_DBFS;
            return Mathf.Clamp(20f * Mathf.Log10(rms), MIN_DBFS, 0f);
        }
    }
}
