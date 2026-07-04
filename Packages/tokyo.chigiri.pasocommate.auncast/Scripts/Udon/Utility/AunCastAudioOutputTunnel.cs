using System;
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// A/B 2 系統の AunCastSpeaker 出力（AVPro シンク）を GetOutputData で読み取り、
    /// AudioClip 再生に変換して通常の Unity AudioSource フィルタチェーンへ渡す互換トンネル。
    ///
    /// 書き込みは DSP クロック（AudioSettings.dspTime）に同期し、フレーム落ちで大量に遅延した場合や
    /// 書込ヘッドが再生ヘッドへ追いつく/追い越す場合はバッファをリセットして復帰する
    /// （参考実装 TopazChat AudioOutputTunnel と同じリングバッファ手法）。
    ///
    /// 注意: inputA と inputB は無条件に加算する。GetOutputData は AudioSource.volume の影響を受ける（検証済み）ため、
    /// AunCast が Standby 側を volume 0 にすることで A+B ≒ Active となり、クロスフェードも自然に反映される。
    /// 出力遅延はリングバッファ分（bufferLength サンプル）増える。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastAudioOutputTunnel : UdonSharpBehaviour
    {
        [Header("AunCast Inputs")]
        [Tooltip("PlayerA 側の AunCastSpeaker AudioSource")]
        public AudioSource inputA;

        [Tooltip("PlayerB 側の AunCastSpeaker AudioSource")]
        public AudioSource inputB;

        [Header("Tunnel Outputs")]
        [Tooltip("左チャンネルを出力する AudioSource")]
        public AudioSource leftOutput;

        [Tooltip("右チャンネルを出力する AudioSource")]
        public AudioSource rightOutput;

        [Tooltip("ステレオを出力する AudioSource")]
        public AudioSource stereoOutput;

        [Header("Buffer")]
        [Tooltip("GetOutputData で読み取るサンプル数。出力遅延はこの値に比例して増える。")]
        [SerializeField] private int bufferLength = 4096;

        [Tooltip("トンネル出力に乗算するゲイン")]
        [Range(0f, 2f)]
        [SerializeField] private float outputGain = 1f;

        [Tooltip("Start 時に出力 AudioSource を再生する")]
        [SerializeField] private bool playOnStart = true;

        [Tooltip("右チャンネルも読み取る。無効時は左チャンネルを複製する。")]
        [SerializeField] private bool readRightChannel = true;

        private const int MIN_BUFFER_LENGTH = 256;
        private const int STEREO_CHANNELS = 2;

        private int _outputSampleRate;
        private int _outputClipFrames;
        private long _previousDspSample = -1;

        private int _leftHead;
        private int _rightHead;
        private int _stereoHead;

        private float[] _readA0;
        private float[] _readA1;
        private float[] _readB0;
        private float[] _readB1;
        private float[] _monoLeft;
        private float[] _monoRight;
        private float[] _stereoInterleaved;

        private AudioClip _leftClip;
        private AudioClip _rightClip;
        private AudioClip _stereoClip;
        private bool _initialized;
        private float _lastWarnAt;

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (_initialized && playOnStart)
                PlayOutputs();
        }

        private void OnDisable()
        {
            StopOutputs();
        }

        private void Initialize()
        {
            if (bufferLength < MIN_BUFFER_LENGTH)
                bufferLength = MIN_BUFFER_LENGTH;

            _outputSampleRate = AudioSettings.outputSampleRate;
            if (_outputSampleRate <= 0)
                _outputSampleRate = 48000;
            _outputClipFrames = bufferLength * 2;

            _readA0 = new float[bufferLength];
            _readA1 = new float[bufferLength];
            _readB0 = new float[bufferLength];
            _readB1 = new float[bufferLength];
            _monoLeft = new float[bufferLength];
            _monoRight = new float[bufferLength];
            _stereoInterleaved = new float[bufferLength * STEREO_CHANNELS];

            _leftClip = AudioClip.Create("AunCastTunnel_Left", _outputClipFrames, 1, _outputSampleRate, false);
            _rightClip = AudioClip.Create("AunCastTunnel_Right", _outputClipFrames, 1, _outputSampleRate, false);
            _stereoClip = AudioClip.Create("AunCastTunnel_Stereo", _outputClipFrames, STEREO_CHANNELS, _outputSampleRate, false);

            AssignClip(leftOutput, _leftClip);
            AssignClip(rightOutput, _rightClip);
            AssignClip(stereoOutput, _stereoClip);

            _previousDspSample = -1;
            _leftHead = 0;
            _rightHead = 0;
            _stereoHead = 0;
            _initialized = _leftClip != null || _rightClip != null || _stereoClip != null;

            if (_initialized && playOnStart)
                PlayOutputs();
        }

        private void Update()
        {
            if (!_initialized)
                Initialize();
            if (!_initialized) return;

            long currentDspSample = (long)Math.Floor(AudioSettings.dspTime * _outputSampleRate);
            if (_previousDspSample < 0)
            {
                _previousDspSample = currentDspSample;
                return;
            }

            int fresh = (int)(currentDspSample - _previousDspSample);
            if (fresh <= 0) return;
            if (fresh > bufferLength)
            {
                // メインスレッドが停止しすぎた。バッファをクリアして再スタートする。
                _previousDspSample = currentDspSample;
                ResetOutputs();
                WarnIfInputsMissing();
                return;
            }

            _previousDspSample = currentDspSample;

            int readBegin = bufferLength - fresh;
            ReadInput(inputA, _readA0, _readA1);
            ReadInput(inputB, _readB0, _readB1);

            float gain = Mathf.Clamp(outputGain, 0f, 2f);
            for (int k = 0; k < fresh; k++)
            {
                int src = readBegin + k;
                float l = (_readA0[src] + _readB0[src]) * gain;
                float r = (_readA1[src] + _readB1[src]) * gain;
                _monoLeft[k] = l;
                _monoRight[k] = r;
                int stereoIndex = k * STEREO_CHANNELS;
                _stereoInterleaved[stereoIndex] = l;
                _stereoInterleaved[stereoIndex + 1] = r;
            }

            WriteMonoOutput(leftOutput, _leftClip, _monoLeft, fresh, true);
            WriteMonoOutput(rightOutput, _rightClip, _monoRight, fresh, false);
            WriteStereoOutput(stereoOutput, _stereoClip, _stereoInterleaved, fresh);

            WarnIfInputsMissing();
        }

        public void RestartOutputs()
        {
            ResetOutputs();
        }

        private void ResetOutputs()
        {
            _leftHead = 0;
            _rightHead = 0;
            _stereoHead = 0;
            StopOutputs();
        }

        // --- 出力書き込み（参考実装のリングバッファ衝突検知を踏襲） ---

        private void WriteMonoOutput(AudioSource output, AudioClip clip, float[] mono, int fresh, bool isLeft)
        {
            if (output == null || clip == null) return;

            int head = isLeft ? _leftHead : _rightHead;

            if (!output.gameObject.activeInHierarchy)
            {
                head = 0;
            }
            else
            {
                if (output.isPlaying && IsRingExhausted(head, output.timeSamples))
                {
                    // 書込ヘッドが再生ヘッドに追いつかれた。停止してリセットする。
                    head = 0;
                    output.Stop();
                    if (isLeft) _leftHead = head; else _rightHead = head;
                    return;
                }

                clip.SetData(mono, head);
                head += fresh;

                if (!output.isPlaying && head >= bufferLength)
                    output.Play();

                if (head >= _outputClipFrames)
                    head -= _outputClipFrames;
            }

            if (isLeft) _leftHead = head; else _rightHead = head;
        }

        private void WriteStereoOutput(AudioSource output, AudioClip clip, float[] interleaved, int fresh)
        {
            if (output == null || clip == null) return;

            int head = _stereoHead;

            if (!output.gameObject.activeInHierarchy)
            {
                head = 0;
            }
            else
            {
                if (output.isPlaying && IsRingExhausted(head, output.timeSamples))
                {
                    head = 0;
                    output.Stop();
                    _stereoHead = head;
                    return;
                }

                clip.SetData(interleaved, head);
                head += fresh;

                if (!output.isPlaying && head >= bufferLength)
                    output.Play();

                if (head >= _outputClipFrames)
                    head -= _outputClipFrames;
            }

            _stereoHead = head;
        }

        /// <summary>書込ヘッドが再生ヘッドを追い越す（リングバッファ枯渇）かどうかを判定する。</summary>
        private bool IsRingExhausted(int head, int timeSamples)
        {
            return (head <= timeSamples && timeSamples < head + bufferLength)
                || (timeSamples < head && timeSamples + _outputClipFrames < head + bufferLength);
        }

        // --- 入出力ユーティリティ ---

        private void ReadInput(AudioSource source, float[] left, float[] right)
        {
            if (source == null || left == null || right == null || !source.enabled)
            {
                Clear(left);
                Clear(right);
                return;
            }

            source.GetOutputData(left, 0);
            if (readRightChannel)
                source.GetOutputData(right, 1);
            else
                CopyBuffer(left, right);
        }

        private void AssignClip(AudioSource output, AudioClip clip)
        {
            if (output == null || clip == null) return;
            output.clip = clip;
            output.loop = true;
        }

        private void PlayOutputs()
        {
            PlayOutput(leftOutput);
            PlayOutput(rightOutput);
            PlayOutput(stereoOutput);
        }

        private void PlayOutput(AudioSource output)
        {
            if (output == null || output.clip == null) return;
            if (!output.isPlaying)
                output.Play();
        }

        private void StopOutputs()
        {
            StopOutput(leftOutput);
            StopOutput(rightOutput);
            StopOutput(stereoOutput);
        }

        private void StopOutput(AudioSource output)
        {
            if (output == null) return;
            if (output.isPlaying)
                output.Stop();
        }

        private void WarnIfInputsMissing()
        {
            if (inputA == null && inputB == null && Time.time - _lastWarnAt > 5.0f)
            {
                _lastWarnAt = Time.time;
                Debug.LogWarning("[AunCast/AudioOutputTunnel] inputA/inputB are not assigned.", this);
            }
        }

        private void Clear(float[] buffer)
        {
            if (buffer == null) return;
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = 0f;
        }

        private void CopyBuffer(float[] source, float[] destination)
        {
            if (source == null || destination == null) return;
            int count = Mathf.Min(source.Length, destination.Length);
            for (int i = 0; i < count; i++)
                destination[i] = source[i];
        }
    }
}
