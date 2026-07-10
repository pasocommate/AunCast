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

            AudioSource desired = SelectAudibleInput();
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
