using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// ワールドに既存の AudioOutputTunnel（TopazChat Player 付属の PCM トンネル）を
    /// AunCast の A/B 2 系統構成へ適応させるアダプタ。
    ///
    /// PCM の読み出し・リングバッファ書き込みは一切行わず、トンネル処理はすべて委譲先の
    /// AudioOutputTunnel に任せる。本コンポーネントは委譲先の input 変数を、A/B のうち
    /// 可聴側（AudioSource.volume が大きい側）の AunCastSpeaker AudioSource へ動的に
    /// 差し替えることだけを担う。AunCast は Standby 側の volume を 0 にするため、
    /// volume の大小比較で Active 側を特定できる。
    ///
    /// 注意: 委譲先の入力は常に 1 系統のため、クロスフェード中の A+B 合成はトンネル経由の
    /// 出力には反映されず、A/B の音量が逆転した時点でのハード切替になる。
    ///
    /// また、停止(Stop All)時は委譲先トンネルのリングバッファに直前の PCM が残り、短いループ音が
    /// 鳴り続ける。これを避けるため、controller が停止(IDLE)を報告している間は input を無音側
    /// (volume 0) の AudioSource へ差し替え、無音を流し込んでバッファをクリアする。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AunCastAudioOutputTunnel : UdonSharpBehaviour
    {
        [Header("AunCast Inputs")]
        [Tooltip("PlayerA 側の AunCastSpeaker AudioSource")]
        public AudioSource inputA;

        [Tooltip("PlayerB 側の AunCastSpeaker AudioSource")]
        public AudioSource inputB;

        [Header("Delegation Target")]
        [Tooltip("委譲先の AudioOutputTunnel（TopazChat Player 付属）。この input 変数を A/B の可聴側へ差し替える。")]
        [SerializeField] private UdonSharpBehaviour targetTunnel;

        [Header("Playback State")]
        [Tooltip("再生状態(IDLE 判定)を参照する AunCastDualPlayerController。停止時に委譲先トンネルへ無音入力を流し込み、リングバッファに残る短いループ音をクリアする。未設定でも従来通り動作するが、停止時のループ音クリアは無効になる。")]
        [SerializeField] private AunCastDualPlayerController controller;

        /// <summary>委譲先 AudioOutputTunnel の入力変数名。</summary>
        private const string TUNNEL_INPUT_VARIABLE = "input";
        private const float WARN_INTERVAL_SEC = 5.0f;

        private AudioSource _currentInput;
        private float _lastWarnAt;

        private void OnEnable()
        {
            // 無効化中に構成が変わっている可能性があるため、再有効化時に必ず差し替え直す
            _currentInput = null;
        }

        private void Update()
        {
            if (targetTunnel == null)
            {
                WarnThrottled("targetTunnel (AudioOutputTunnel) is not assigned.");
                return;
            }

            // 停止(IDLE)中は、委譲先トンネルのリングバッファに残る短いループ音をクリアするため、
            // GetOutputData が全ゼロを返す無音側(volume≈0)の入力へ差し替える。委譲先トンネルは
            // 入力を GetOutputData（volume 反映済み）で読み取るため、無音入力を流し続けることで
            // バッファが無音で上書きされる（Rewire.cs のシンク不可聴化コメント参照）。
            AudioSource desired = IsPlaybackStopped() ? SelectSilentInput() : SelectAudibleInput();
            if (desired == null)
            {
                WarnThrottled("inputA/inputB are not assigned.");
                return;
            }

            if (desired == _currentInput) return;
            targetTunnel.SetProgramVariable(TUNNEL_INPUT_VARIABLE, desired);
            _currentInput = desired;
        }

        /// <summary>
        /// A/B のうち可聴側の入力を選ぶ。音量が同値の間は現在の選択を維持し、
        /// クロスフェード中に選択が行き来しないようにする。
        /// </summary>
        private AudioSource SelectAudibleInput()
        {
            bool aAvailable = IsAvailable(inputA);
            bool bAvailable = IsAvailable(inputB);
            if (!aAvailable) return bAvailable ? inputB : null;
            if (!bAvailable) return inputA;

            if (_currentInput == inputB)
                return inputA.volume > inputB.volume ? inputA : inputB;
            return inputB.volume > inputA.volume ? inputB : inputA;
        }

        /// <summary>
        /// リングバッファのフラッシュ用に、A/B のうち音量が最小（＝無音側）の入力を選ぶ。
        /// 停止中は Standby 側が volume 0 になっており、その GetOutputData は全ゼロを返すため、
        /// これを委譲先トンネルへ流すことでバッファに残った音を無音で押し出せる。
        /// </summary>
        private AudioSource SelectSilentInput()
        {
            bool aAvailable = IsAvailable(inputA);
            bool bAvailable = IsAvailable(inputB);
            if (!aAvailable) return bAvailable ? inputB : null;
            if (!bAvailable) return inputA;

            return inputA.volume <= inputB.volume ? inputA : inputB;
        }

        /// <summary>
        /// AunCast が停止(IDLE)状態かを返す。controller 未設定時は false を返し、従来通り可聴側追従のみを行う。
        /// </summary>
        private bool IsPlaybackStopped()
        {
            return controller != null
                && controller.GetLocalState() == AunCastDualPlayerController.STATE_IDLE;
        }

        private bool IsAvailable(AudioSource source)
        {
            return source != null && source.enabled && source.gameObject.activeInHierarchy;
        }

        private void WarnThrottled(string message)
        {
            if (Time.time - _lastWarnAt <= WARN_INTERVAL_SEC) return;
            _lastWarnAt = Time.time;
            Debug.LogWarning("[AunCast/AudioOutputTunnel] " + message, this);
        }
    }
}
