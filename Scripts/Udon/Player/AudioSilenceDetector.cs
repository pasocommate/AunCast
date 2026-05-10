
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// 各 AVPro AudioSource にアタッチし、AudioSource.GetOutputData() で
    /// メインスレッドから PCM を取得して RMS 無音検知を行うコンポーネント。
    ///
    /// 注意: OnAudioFilterRead 内のフィールド書き込みは Udon VM ヒープと分離されており
    /// メインスレッドから読めないため、GetOutputData を使用する（VRChat-Udon-Development-Notes 9.6 参照）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AudioSilenceDetector : UdonSharpBehaviour
    {
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

        /// <summary>直近の RMS 値。デバッグ UI 表示用にキャッシュ。</summary>
        private float _lastRms;

        /// <summary>直近の計測で使用したサンプル数。UI 側でバッファサイズを表示するため。</summary>
        private int _lastRmsSampleCount;
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

            _audioSource.GetOutputData(_outputBuffer, 0);
            _lastRmsSampleCount = _outputBuffer.Length;

            float acc = 0f;
            for (int i = 0; i < _outputBuffer.Length; i++)
                acc += _outputBuffer[i] * _outputBuffer[i];

            float rms = Mathf.Sqrt(acc / _outputBuffer.Length);
            _lastRms = rms;
            _lastRmsFrame = Time.frameCount;
            return rms;
        }

        // --- Getters ---

        /// <summary>無音と判定するのに必要な連続秒数。自動リシンク判定で使用。</summary>
        public float GetSilenceConsecutiveSec() { return silenceConsecutiveSec; }

        /// <summary>無音と見なす RMS 閾値（リニア）。dBFS から変換して返す。</summary>
        public float GetSilenceRmsThreshold() { return Mathf.Pow(10f, silenceRmsThresholdDbfs / 20f); }
        public float GetSilenceRmsThresholdDbfs() { return Mathf.Clamp(silenceRmsThresholdDbfs, MIN_DBFS, 0f); }

        /// <summary>直近の RMS 値をデバッグ UI に公開する。</summary>
        public float GetLastRms() { return _lastRms; }
        public float GetLastRmsDbfs()
        {
            float rms = GetRms();
            if (rms <= DBFS_EPSILON) return MIN_DBFS;
            return Mathf.Clamp(20f * Mathf.Log10(rms), MIN_DBFS, 0f);
        }

        /// <summary>直近の計測サンプル数をデバッグ UI に公開する。</summary>
        public int GetLastRmsSampleCount() { return _lastRmsSampleCount; }
    }
}
