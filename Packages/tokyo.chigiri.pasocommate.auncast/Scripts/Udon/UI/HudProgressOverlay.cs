using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// VR ジェスチャー長押し中に視界へ重ねるプログレス表示。
    /// 頭部追従の World Space Quad に "RenderMate/HUD/Progress" シェーダーを貼り、
    /// LateUpdate で位置と進捗値を毎フレーム反映する。
    /// 形状（Bar/Pie）やマテリアルパラメータはエディタ時に確定済み。
    /// 表示はローカル限定のため同期は持たない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class HudProgressOverlay : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform quadTransform;
        [SerializeField] private MeshRenderer quadRenderer;

        [Header("Placement (head-local, configurable from Theme)")]
        [Tooltip("頭部ローカル座標における HUD 配置オフセット (m)")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, -0.18f, 0.6f);

        [Header("Behavior (configurable from Settings)")]
        [Tooltip("HUD を出し始めるまでの猶予 (秒)。これ以下の進捗では表示しない。")]
        [SerializeField] private float showThreshold = 0.15f;

        [Tooltip("長押し成立 / キャンセル後にフェードアウトする秒数")]
        [SerializeField] private float fadeOutDuration = 0.18f;

        // ランタイム状態
        /// <summary>HUD が表示中かどうか。true の間は LateUpdate で位置と進捗を更新し続ける。</summary>
        private bool _showing;
        private float _currentProgress;
        private float _fadeOutElapsed;
        /// <summary>フェードアウト中かどうか。Hide() で true になり、fadeOutDuration 経過後に非表示化する。</summary>
        private bool _fading;
        /// <summary>
        /// Renderer.material アクセスで生成されるこのレンダラ固有のマテリアルインスタンス。
        /// SharedMaterial を書き換えると他プレイヤーの HUD にも影響するため、必ずこの経由で操作する。
        /// </summary>
        private Material _runtimeMaterial;

        private void Start()
        {
            if (quadRenderer != null)
                _runtimeMaterial = quadRenderer.material;

            SetVisible(false);
        }

        /// <summary>
        /// 毎フレーム末尾でヘッドトラッキングに追従させ、プログレス値をシェーダーに反映する。
        /// LateUpdate を使うことでカメラ移動後の正確な頭部位置に配置できる。
        /// </summary>
        private void LateUpdate()
        {
            if (!_showing && !_fading) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            if (local.IsUserInVR())
            {
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                transform.position = head.position + head.rotation * localOffset;
                transform.rotation = head.rotation;
            }
            else
            {
                var cam = VRCCameraSettings.ScreenCamera;
                float dist = localOffset.z;
                transform.position = cam.Position + cam.Rotation * new Vector3(0f, 0f, dist);
                transform.rotation = cam.Rotation;
            }

            if (_fading)
            {
                _fadeOutElapsed += Time.deltaTime;
                if (_fadeOutElapsed >= fadeOutDuration)
                {
                    SetVisible(false);
                    _fading = false;
                    return;
                }
            }

            if (_runtimeMaterial != null)
                _runtimeMaterial.SetFloat("_Progress", _currentProgress);
        }

        /// <summary>
        /// 経過時間とホールド総時間からプログレスを更新する。
        /// 表示閾値に達するまでは何も出さない。
        /// </summary>
        public void SetHoldProgress(float elapsed, float duration)
        {
            if (elapsed < showThreshold)
            {
                if (_showing) SetVisible(false);
                return;
            }

            float remaining = duration - showThreshold;
            float t = (remaining > 1e-5f) ? Mathf.Clamp01((elapsed - showThreshold) / remaining) : 0f;
            _currentProgress = t;
            _fading = false;
            _fadeOutElapsed = 0f;
            if (!_showing) SetVisible(true);
        }

        /// <summary>長押し成立 / キャンセル時に呼び、フェードアウトを開始する。</summary>
        public void Hide()
        {
            if (!_showing)
            {
                _fading = false;
                return;
            }
            if (fadeOutDuration <= 0f)
            {
                SetVisible(false);
                return;
            }
            _fading = true;
            _fadeOutElapsed = 0f;
        }

        /// <summary>
        /// Renderer の enabled を切り替えて表示/非表示を制御する。
        /// GameObject の SetActive ではなく Renderer 単位で操作することで、
        /// Update/LateUpdate のコールバックを維持したままレンダリングコストだけを除外する。
        /// </summary>
        private void SetVisible(bool visible)
        {
            _showing = visible;
            if (quadRenderer != null)
                quadRenderer.enabled = visible;
        }
    }
}
