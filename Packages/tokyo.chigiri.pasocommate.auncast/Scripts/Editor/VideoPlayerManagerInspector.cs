
using UnityEditor;
using UdonSharpEditor;

namespace PasocomMate.AunCast.Internal
{
    /// <summary>
    /// VideoPlayerManager の Inspector カスタムエディタ。
    /// 配線漏れや参照切れをビルド前に検出できるよう、検証結果を Inspector 上に表示する。
    /// </summary>
    [CustomEditor(typeof(VideoPlayerManager))]
    internal class VideoPlayerManagerInspector : Editor
    {
        private SerializedProperty receiverProperty;
        private SerializedProperty avProVideoProperty;
        private SerializedProperty avProRendererProperty;
        private SerializedProperty audioSourcesProperty;

        private void OnEnable()
        {
            receiverProperty = serializedObject.FindProperty(nameof(VideoPlayerManager.receiver));
            avProVideoProperty = serializedObject.FindProperty(nameof(VideoPlayerManager.avProPlayer));
            avProRendererProperty = serializedObject.FindProperty(nameof(VideoPlayerManager.avProTextureRenderer));
            audioSourcesProperty = serializedObject.FindProperty(nameof(VideoPlayerManager.audioSources));
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawProgramSource(target, false)) return;

            EditorGUILayout.HelpBox("このゲームオブジェクト上のビデオプレイヤーを直接変更しないでください。すべての変更はLocalDualPlayerControllerで行う必要があります。これらの設定を変更すると、動作が壊れます。", MessageType.Warning);
            EditorGUILayout.PropertyField(receiverProperty);
            EditorGUILayout.PropertyField(avProVideoProperty);
            EditorGUILayout.PropertyField(avProRendererProperty);
            EditorGUILayout.PropertyField(audioSourcesProperty, true);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
