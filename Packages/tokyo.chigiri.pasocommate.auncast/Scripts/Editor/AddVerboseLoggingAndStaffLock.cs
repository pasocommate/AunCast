using System;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// AunCast.prefab に VerboseLogging トグル（Issue #12）と StaffLock ボタン（Issue #16）を追加する
    /// セットアップスクリプト。適用後は Tools > UdonSharp > Refresh All UdonSharp Programs を実行すること。
    /// </summary>
    internal static class AddVerboseLoggingAndStaffLock
    {
        private const string AUNCAST_GUID = "0d617877712537147a584eb2a1ef735c";

        [MenuItem("Tools/AunCast/Add VerboseLogging and StaffLock")]
        internal static void Execute()
        {
            string path = AssetDatabase.GUIDToAssetPath(AUNCAST_GUID);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] AunCast.prefab が見つかりません (GUID: " + AUNCAST_GUID + ")");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool ok = false;
            try
            {
                ok = Patch(root);
                if (ok)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            catch (Exception e)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] パッチ中にエラー: " + e);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log("[AddVerboseLoggingAndStaffLock] " + (ok ? "OK" : "エラー（詳細はConsole参照）"));
            AssetDatabase.Refresh();
        }

        private static bool Patch(GameObject root)
        {
            UserStatusPanel panel = root.GetComponentInChildren<UserStatusPanel>(true);
            if (panel == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] UserStatusPanel が見つかりません");
                return false;
            }

            var panelSo = new SerializedObject(panel);

            // 既に配線済みならスキップ
            var existingVerbose = panelSo.FindProperty("verboseLoggingToggle");
            if (existingVerbose != null && existingVerbose.objectReferenceValue != null)
            {
                Debug.Log("[AddVerboseLoggingAndStaffLock] 既に適用済みです。スキップします。");
                return true;
            }

            bool verboseOk = AddVerboseLoggingToggle(root, panel, panelSo);
            bool lockOk = AddStaffLockButton(root, panel, panelSo);

            if (!verboseOk || !lockOk)
                return false;

            panelSo.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(panel);
            return true;
        }

        // ─── VerboseLogging Toggle（StaffPadded 内）─────────────────────

        private static bool AddVerboseLoggingToggle(GameObject root, UserStatusPanel panel, SerializedObject panelSo)
        {
            Transform staffPadded = FindDescendant(root.transform, "StaffPadded");
            if (staffPadded == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] StaffPadded が見つかりません");
                return false;
            }

            Transform autoResyncToggle = FindDescendant(root.transform, "AutoResyncToggle");
            if (autoResyncToggle == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] AutoResyncToggle が見つかりません");
                return false;
            }

            GameObject verboseGo = UnityEngine.Object.Instantiate(autoResyncToggle.gameObject, staffPadded);
            verboseGo.name = "VerboseLoggingToggle";

            // ラベルテキストを変更（\uE868 = bug_report アイコン）
            TMP_Text verboseLabel = FindLabelText(verboseGo);
            if (verboseLabel != null)
                verboseLabel.text = "\uE868 Verbose Log";

            // 位置を設定（anchor(0,0) 基準、ConnectionMaxLabel の上に配置）
            RectTransform verboseRect = verboseGo.GetComponent<RectTransform>();
            if (verboseRect != null)
            {
                verboseRect.anchorMin = Vector2.zero;
                verboseRect.anchorMax = Vector2.zero;
                verboseRect.pivot = Vector2.zero;
                verboseRect.anchoredPosition = new Vector2(0f, 249f);
                verboseRect.sizeDelta = new Vector2(240f, 38f);
            }

            // EventTrigger のホバーイベントをクリア（VerboseLog にはホバー表示不要）
            var et = verboseGo.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (et != null)
                et.triggers.Clear();

            Toggle verboseToggle = verboseGo.GetComponent<Toggle>();
            if (verboseToggle == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] VerboseLoggingToggle の Toggle コンポーネントが見つかりません");
                return false;
            }

            var verboseProp = panelSo.FindProperty("verboseLoggingToggle");
            if (verboseProp != null)
                verboseProp.objectReferenceValue = verboseToggle;

            Debug.Log("[AddVerboseLoggingAndStaffLock] VerboseLoggingToggle 追加: OK");
            return true;
        }

        // ─── StaffLock Button（TopBarPadded 内）──────────────────────────

        private static bool AddStaffLockButton(GameObject root, UserStatusPanel panel, SerializedObject panelSo)
        {
            Transform topBarPadded = FindDescendant(root.transform, "TopBarPadded");
            if (topBarPadded == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] TopBarPadded が見つかりません");
                return false;
            }

            Transform switchViewButton = topBarPadded.Find("SwitchViewButton");
            if (switchViewButton == null)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] SwitchViewButton が見つかりません");
                return false;
            }

            GameObject lockGo = UnityEngine.Object.Instantiate(switchViewButton.gameObject, topBarPadded);
            lockGo.name = "StaffLockButton";

            // 位置を SwitchViewButton（-64, 0）のさらに左（-128, 0）に設定
            RectTransform lockRect = lockGo.GetComponent<RectTransform>();
            if (lockRect != null)
            {
                lockRect.anchorMin = Vector2.one;
                lockRect.anchorMax = Vector2.one;
                lockRect.pivot = Vector2.one;
                lockRect.anchoredPosition = new Vector2(-128f, 0f);
                lockRect.sizeDelta = new Vector2(56f, 56f);
            }

            // Inner GO を取得・有効化
            if (lockGo.transform.childCount == 0)
            {
                Debug.LogError("[AddVerboseLoggingAndStaffLock] StaffLockButton に子 GO がありません");
                return false;
            }

            Transform lockInner = lockGo.transform.GetChild(0);
            lockInner.name = "StaffLockButton_Inner";
            lockInner.gameObject.SetActive(true);

            // Button の onClick を OnStaffLockButtonPress に変更
            Button lockBtn = lockInner.GetComponent<Button>();
            if (lockBtn != null)
            {
                var btnSo = new SerializedObject(lockBtn);
                const string callsPath = "m_OnClick.m_PersistentCalls.m_Calls";
                var callsProp = btnSo.FindProperty(callsPath);
                if (callsProp != null && callsProp.arraySize > 0)
                {
                    var call = callsProp.GetArrayElementAtIndex(0);
                    var strArg = call.FindPropertyRelative("m_Arguments.m_StringArgument");
                    if (strArg != null)
                        strArg.stringValue = "OnStaffLockButtonPress";
                }
                btnSo.ApplyModifiedProperties();
            }

            // EventTrigger を除去（ロックボタンにはホバーイベント不要）
            var lockEt = lockInner.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (lockEt != null)
                UnityEngine.Object.DestroyImmediate(lockEt);

            // ラベルテキストをロックアイコン（\uE899 = lock）に変更
            TMP_Text lockLabel = lockInner.GetComponentInChildren<TMP_Text>(true);
            if (lockLabel != null)
                lockLabel.text = "\uE899";

            var lockBtnProp = panelSo.FindProperty("staffLockButton");
            if (lockBtnProp != null)
                lockBtnProp.objectReferenceValue = lockBtn;

            var lockLabelProp = panelSo.FindProperty("staffLockButtonLabel");
            if (lockLabelProp != null)
                lockLabelProp.objectReferenceValue = lockLabel;

            Debug.Log("[AddVerboseLoggingAndStaffLock] StaffLockButton 追加: OK");
            return true;
        }

        // ─── ユーティリティ ──────────────────────────────────────────────

        /// <summary>名前で子孫 Transform を深さ優先探索する。</summary>
        private static Transform FindDescendant(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Toggle GO 内の "Label" 子 GO にある TMP_Text を返す。</summary>
        private static TMP_Text FindLabelText(GameObject toggleGo)
        {
            for (int i = 0; i < toggleGo.transform.childCount; i++)
            {
                Transform child = toggleGo.transform.GetChild(i);
                if (child.name == "Label")
                {
                    var tmp = child.GetComponent<TMP_Text>();
                    if (tmp != null) return tmp;
                }
            }
            return toggleGo.GetComponentInChildren<TMP_Text>(true);
        }
    }
}
