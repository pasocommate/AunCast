#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using VRC.SDK3.Video.Components.AVPro;

namespace PasocomMate.AunCast.Internal
{
    public partial class AunCastSettingsInspector
    {
        // ── 利用規約 同意ゲート ──

        private static string _cachedTermsHash;
        private static string _cachedTermsSourceSignature;

        /// <summary>
        /// 利用規約に同意済みなら true（設定 UI を続行）。未同意なら同意ゲートを描画して false。
        /// </summary>
        private bool DrawConsentGateIfNeeded()
        {
            string version = GetCurrentPackageVersion();
            if (!TryGetTermsHash(out string termsHash, out string errorMessage))
            {
                EditorGUILayout.HelpBox(
                    AunCastEditorLocalization.Localize(
                        $"利用規約ファイルを読み取れないため、同意状態を確認できません。\n{errorMessage}",
                        $"The Terms of Use files could not be read, so the agreement status cannot be verified.\n{errorMessage}"),
                    MessageType.Error);
                return false;
            }

            if (AunCastProjectSettingsStore.HasConsented(termsHash)
                || AunCastProjectSettingsStore.TryMigrateLegacyConsent(termsHash))
                return true;

            DrawConsentGate(version, termsHash);
            return false;
        }

        /// <summary>日本語・英語の規約PDFを固定順で結合し、SHA-256を返す。</summary>
        private static bool TryGetTermsHash(out string termsHash, out string errorMessage)
        {
            termsHash = string.Empty;
            errorMessage = string.Empty;

            try
            {
                string japanesePath = AssetDatabase.GUIDToAssetPath(VN3_LICENSE_JA_GUID);
                string englishPath = AssetDatabase.GUIDToAssetPath(VN3_LICENSE_EN_GUID);
                if (string.IsNullOrEmpty(japanesePath) || string.IsNullOrEmpty(englishPath))
                {
                    errorMessage = AunCastEditorLocalization.Localize(
                        "利用規約ファイルが見つかりません。",
                        "A Terms of Use file was not found.");
                    return false;
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string japaneseFullPath = Path.Combine(projectRoot, japanesePath);
                string englishFullPath = Path.Combine(projectRoot, englishPath);
                if (!File.Exists(japaneseFullPath) || !File.Exists(englishFullPath))
                {
                    errorMessage = AunCastEditorLocalization.Localize(
                        "利用規約ファイルが見つかりません。",
                        "A Terms of Use file was not found.");
                    return false;
                }

                string sourceSignature = GetTermsSourceSignature(japaneseFullPath, englishFullPath);
                if (!string.IsNullOrEmpty(_cachedTermsHash) &&
                    _cachedTermsSourceSignature == sourceSignature)
                {
                    termsHash = _cachedTermsHash;
                    return true;
                }

                using (var hashInput = new MemoryStream())
                {
                    AppendHashInput(hashInput, VN3_LICENSE_JA_GUID, japaneseFullPath);
                    AppendHashInput(hashInput, VN3_LICENSE_EN_GUID, englishFullPath);

                    using (SHA256 sha256 = SHA256.Create())
                    {
                        termsHash = BitConverter.ToString(sha256.ComputeHash(hashInput.ToArray()))
                            .Replace("-", string.Empty);
                    }
                }

                _cachedTermsHash = termsHash;
                _cachedTermsSourceSignature = sourceSignature;
                return true;
            }
            catch (System.Exception e)
            {
                errorMessage = e.Message;
                return false;
            }
        }

        private static string GetTermsSourceSignature(string japaneseFullPath, string englishFullPath)
        {
            var japaneseInfo = new FileInfo(japaneseFullPath);
            var englishInfo = new FileInfo(englishFullPath);
            return $"{japaneseInfo.Length}:{japaneseInfo.LastWriteTimeUtc.Ticks}:" +
                $"{englishInfo.Length}:{englishInfo.LastWriteTimeUtc.Ticks}";
        }

        private static void AppendHashInput(Stream hashInput, string fileGuid, string fullPath)
        {
            byte[] guidBytes = Encoding.UTF8.GetBytes(fileGuid);
            byte[] contentBytes = File.ReadAllBytes(fullPath);
            WriteHashPart(hashInput, guidBytes);
            WriteHashPart(hashInput, contentBytes);
        }

        private static void WriteHashPart(Stream hashInput, byte[] bytes)
        {
            byte[] lengthBytes = System.BitConverter.GetBytes(bytes.Length);
            hashInput.Write(lengthBytes, 0, lengthBytes.Length);
            hashInput.Write(bytes, 0, bytes.Length);
        }

        private void DrawConsentGate(string version, string termsHash)
        {
            EditorGUILayout.Space(8);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.LabelField(
                AunCastEditorLocalization.Localize("利用規約への同意", "Terms of Use"),
                titleStyle);

            EditorGUILayout.HelpBox(
                AunCastEditorLocalization.Localize(
                    "AunCast を使用するには利用規約（VN3 ライセンス）への同意が必要です｡ 下のボタンから規約全文を開いて内容を確認し､ 同意のうえ設定を続けてください｡ 同意するまで設定項目は表示されません｡",
                    "Using AunCast requires agreement to the Terms of Use (VN3 License). Open the full terms with the buttons below, review them, then agree to continue. Settings stay hidden until you agree."),
                MessageType.Warning);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("利用規約（日本語）を開く", "Open Terms (Japanese)"),
                    GUILayout.Height(24)))
                {
                    OpenLicensePdf(VN3_LICENSE_JA_GUID);
                }
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("利用規約（English）を開く", "Open Terms (English)"),
                    GUILayout.Height(24)))
                {
                    OpenLicensePdf(VN3_LICENSE_EN_GUID);
                }
            }

            EditorGUILayout.Space(6);

            // 規約を読んだうえでチェック→同意ボタン有効化、の二段階で誤クリックを防ぐ。
            _consentCheckbox = EditorGUILayout.ToggleLeft(
                AunCastEditorLocalization.Localize(
                    "利用規約の内容を確認し、同意します。",
                    "I have read and agree to the Terms of Use."),
                _consentCheckbox);

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(!_consentCheckbox))
            {
                if (GUILayout.Button(
                    AunCastEditorLocalization.Localize("同意して続行", "Agree and Continue"),
                    GUILayout.Height(28)))
                {
                    AunCastProjectSettingsStore.SetConsented(termsHash, version);
                    _consentCheckbox = false;
                    // 描画する UI の構成が変わるため、現フレームの GUI を一旦やり直す。
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(8);
        }

        private static void OpenLicensePdf(string guid)
        {
            var asset = AunCastEditorAssetUtility.LoadAssetByGuid<UnityEngine.Object>(guid);
            if (asset == null)
            {
                EditorUtility.DisplayDialog(
                    "AunCast",
                    AunCastEditorLocalization.Localize(
                        "利用規約ファイルが見つかりませんでした。",
                        "Terms of Use file was not found."),
                    "OK");
                return;
            }

            AssetDatabase.OpenAsset(asset);
        }

    }
}
#endif
