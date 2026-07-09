#if UNITY_EDITOR
using System;
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

        /// <summary>
        /// 利用規約に同意済みなら true（設定 UI を続行）。未同意なら同意ゲートを描画して false。
        /// </summary>
        private bool DrawConsentGateIfNeeded()
        {
            string version = GetCurrentPackageVersion();
            int major = GetMajorVersion(version);
            if (AunCastProjectSettingsStore.HasConsented(major))
                return true;

            DrawConsentGate(version);
            return false;
        }

        private static int GetMajorVersion(string version)
        {
            return TryParseVersion(version, out var parsed) ? parsed.Major : -1;
        }

        private void DrawConsentGate(string version)
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
                    AunCastProjectSettingsStore.SetConsented(GetMajorVersion(version), version);
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
