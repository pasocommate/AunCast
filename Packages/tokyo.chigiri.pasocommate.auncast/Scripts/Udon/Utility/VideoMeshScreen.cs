
using UdonSharp;
using UnityEngine;

namespace PasocomMate.AunCast
{
    /// <summary>
    /// MeshRenderer のマテリアルにビデオテクスチャを適用する。
    /// sharedMaterials を更新するため、同一マテリアルアセットを共有する他のスクリーンにも自動反映される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class VideoMeshScreen : UdonSharpBehaviour
    {
        [Tooltip("このレンダラーのシェーダーがビデオテクスチャに使用するパラメーター名")]
        /// <summary>シェーダーごとにテクスチャプロパティ名が異なるため、設定で切り替え可能にしている。</summary>
        public string texParam = "_EmissionMap";

        [Tooltip("ビデオテクスチャを設定するレンダラーのインデックス")]
        /// <summary>マルチマテリアルのレンダラーで、どのスロットに適用するかを指定する。</summary>
        public int rendererIndex = 0;

        [Header("Debug")]
        [Tooltip("VideoMeshScreen の詳細ログを出力する")]
        public bool verboseLogging = true;

        /// <summary>同一 GameObject 上のレンダラーをキャッシュ。</summary>
        private Renderer targetRenderer;
        /// <summary>重複適用を防ぐため、前回設定したテクスチャを保持。</summary>
        private Texture lastRenderTexture;
        private float _lastNullTextureWarnAt;
        private float _lastMaterialWarnAt;
        private Material _restoreMaterial;
        private string _restoreParam0;
        private Texture _restoreTex0;
        private bool _restoreHas0;
        private string _restoreParam1;
        private Texture _restoreTex1;
        private bool _restoreHas1;
        private string _restoreParam2;
        private Texture _restoreTex2;
        private bool _restoreHas2;

        /// <summary>レンダラーをキャッシュする。</summary>
        private void Start()
        {
            targetRenderer = GetComponent<Renderer>();
            CacheRestoreStateIfNeeded(GetTargetMaterial());
            LogVerbose($"Initialized (renderer={(targetRenderer != null)}, texParam={texParam}, index={rendererIndex})");
        }

        private void OnDisable()
        {
            RestoreMaterialTextures();
        }

        private void OnDestroy()
        {
            RestoreMaterialTextures();
        }

        /// <summary>テクスチャをレンダラーのマテリアルに適用する。変化がなければスキップして負荷を抑える。</summary>
        public void UpdateVideoTexture(Texture renderTexture)
        {
            if (renderTexture == lastRenderTexture)
                return;

            if (renderTexture == null)
            {
                float now = Time.time;
                if (now - _lastNullTextureWarnAt > 2.0f)
                {
                    _lastNullTextureWarnAt = now;
                    LogWarning("UpdateVideoTexture received null texture");
                }
            }

            EnsureRenderer();
            if (targetRenderer)
            {
                Material rendererMat = GetTargetMaterial();
                if (rendererMat != null)
                {
                    CacheRestoreStateIfNeeded(rendererMat);
                    // 設定値を尊重しつつ、主要プロパティにも反映して表示失敗を避ける
                    SetTextureIfPropertyExists(rendererMat, texParam, renderTexture);
                    if (texParam != "_EmissionMap")
                        SetTextureIfPropertyExists(rendererMat, "_EmissionMap", renderTexture);
                    if (texParam != "_MainTex")
                        SetTextureIfPropertyExists(rendererMat, "_MainTex", renderTexture);
                }
            }
            else
            {
                LogWarning("Renderer missing; cannot apply video texture");
            }

            lastRenderTexture = renderTexture;
            LogVerbose($"Texture updated: {(renderTexture != null ? renderTexture.name : "null")}");
        }

        private void EnsureRenderer()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
        }

        /// <summary>rendererIndex で指定された sharedMaterial を取得する。範囲外ならインデックス 0 にフォールバック。</summary>
        private Material GetTargetMaterial()
        {
            if (targetRenderer == null) return null;

            Material[] mats = targetRenderer.sharedMaterials;
            if (mats == null || mats.Length == 0) return null;

            int idx = rendererIndex;
            if (idx < 0 || idx >= mats.Length)
            {
                if (Time.time - _lastMaterialWarnAt > 2.0f)
                {
                    _lastMaterialWarnAt = Time.time;
                    LogWarning($"rendererIndex out of range: {rendererIndex}, fallback to 0");
                }
                idx = 0;
            }

            return mats[idx];
        }

        private void CacheRestoreStateIfNeeded(Material mat)
        {
            if (mat == null) return;
            if (_restoreMaterial != null) return;

            _restoreMaterial = mat;
            CacheSingleTexture(mat, texParam);
            if (texParam != "_EmissionMap")
                CacheSingleTexture(mat, "_EmissionMap");
            if (texParam != "_MainTex")
                CacheSingleTexture(mat, "_MainTex");
        }

        private void CacheSingleTexture(Material mat, string param)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param)) return;
            if (param == _restoreParam0 || param == _restoreParam1 || param == _restoreParam2) return;

            if (!_restoreHas0)
            {
                _restoreParam0 = param;
                _restoreTex0 = mat.GetTexture(param);
                _restoreHas0 = true;
                return;
            }

            if (!_restoreHas1)
            {
                _restoreParam1 = param;
                _restoreTex1 = mat.GetTexture(param);
                _restoreHas1 = true;
                return;
            }

            if (!_restoreHas2)
            {
                _restoreParam2 = param;
                _restoreTex2 = mat.GetTexture(param);
                _restoreHas2 = true;
            }
        }

        private void RestoreMaterialTextures()
        {
            if (_restoreMaterial == null) return;

            if (_restoreHas0 && _restoreMaterial.HasProperty(_restoreParam0))
                _restoreMaterial.SetTexture(_restoreParam0, _restoreTex0);
            if (_restoreHas1 && _restoreMaterial.HasProperty(_restoreParam1))
                _restoreMaterial.SetTexture(_restoreParam1, _restoreTex1);
            if (_restoreHas2 && _restoreMaterial.HasProperty(_restoreParam2))
                _restoreMaterial.SetTexture(_restoreParam2, _restoreTex2);
        }

        /// <summary>プロパティの存在を確認してからテクスチャを設定する。存在しないシェーダーへの誤設定を防ぐ。</summary>
        private void SetTextureIfPropertyExists(Material mat, string param, Texture texture)
        {
            if (mat == null || string.IsNullOrEmpty(param)) return;
            if (!mat.HasProperty(param))
            {
                if (Time.time - _lastMaterialWarnAt > 2.0f)
                {
                    _lastMaterialWarnAt = Time.time;
                    LogWarning($"Material has no property: {param}");
                }
                return;
            }
            mat.SetTexture(param, texture);
        }

        private void LogVerbose(string message)
        {
            if (!verboseLogging) return;
            Debug.Log($"[AunCast/VideoMeshScreen] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[AunCast/VideoMeshScreen] {message}", this);
        }
    }
}
