#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PasocomMate.AunCast.Tests
{
    [TestFixture]
    public class AunCastHoverEventTests
    {
        private const string PREFAB_GUID = "0d617877712537147a584eb2a1ef735c";
        private GameObject _prefab;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(PREFAB_GUID);
            Assert.IsNotEmpty(prefabPath, "プレハブ GUID が見つかりません");
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(_prefab, $"プレハブをロードできません: {prefabPath}");
        }

        [TestCase("PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/ModeDisplayGroup", "OnHoverMode")]
        [TestCase("PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/ModeEditGroup", "OnHoverMode")]
        [TestCase("PortablePanel/ContentScaler/PortableContentArea/UserContent/UserPadded/ModeLabel", "OnHoverMode")]
        [TestCase("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/ForceModeLabel", "OnHoverForceMode")]
        [TestCase("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/ForceModeDisplayGroup", "OnHoverForceMode")]
        [TestCase("PortablePanel/ContentScaler/PortableContentArea/StaffContent/StaffPadded/ForceModeEditGroup", "OnHoverForceMode")]
        public void ModeHoverEvents_InvokeCorrectHelp(string path, string expectedEventName)
        {
            var target = _prefab.transform.Find(path);
            Assert.IsNotNull(target, $"対象 UI が見つかりません: {path}");

            var trigger = target.GetComponent<EventTrigger>();
            Assert.IsNotNull(trigger, $"EventTrigger が見つかりません: {path}");

            Assert.AreEqual(expectedEventName, GetPointerEnterStringArgument(trigger),
                $"PointerEnter の SendCustomEvent 引数が不正です: {path}");
        }

        private static string GetPointerEnterStringArgument(EventTrigger trigger)
        {
            var so = new SerializedObject(trigger);
            var delegates = so.FindProperty("m_Delegates");
            Assert.IsNotNull(delegates, "EventTrigger.m_Delegates が見つかりません");

            for (int i = 0; i < delegates.arraySize; i++)
            {
                var entry = delegates.GetArrayElementAtIndex(i);
                var eventId = entry.FindPropertyRelative("eventID");
                if (eventId == null || eventId.enumValueIndex != (int)EventTriggerType.PointerEnter)
                    continue;

                var calls = entry
                    .FindPropertyRelative("callback")
                    .FindPropertyRelative("m_PersistentCalls")
                    .FindPropertyRelative("m_Calls");
                Assert.IsNotNull(calls, "PointerEnter の PersistentCalls が見つかりません");
                Assert.Greater(calls.arraySize, 0, "PointerEnter の呼び出しが未設定です");

                return calls.GetArrayElementAtIndex(0)
                    .FindPropertyRelative("m_Arguments")
                    .FindPropertyRelative("m_StringArgument")
                    .stringValue;
            }

            Assert.Fail("PointerEnter の EventTrigger エントリが見つかりません");
            return string.Empty;
        }
    }
}
#endif
