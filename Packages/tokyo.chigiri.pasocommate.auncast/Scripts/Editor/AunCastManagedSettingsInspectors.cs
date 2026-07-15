#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;

namespace PasocomMate.AunCast.Internal
{
    internal abstract class AunCastManagedSettingsInspectorBase : Editor
    {
        private readonly bool[] _showManagedPropertyGroups = new bool[3];

        protected virtual string[] SettingsManagedPropertyNames => System.Array.Empty<string>();
        protected virtual string[] ThemeApplierManagedPropertyNames => System.Array.Empty<string>();
        protected virtual string[] WiringPropertyNames => System.Array.Empty<string>();
        private bool ShouldDrawBanner
        {
            get
            {
                if (target == null) return false;
                string typeName = target.GetType().Name;
                return typeName == nameof(AunCastSpeaker)
                    || typeName == nameof(AunCastScreen)
                    || typeName == nameof(AunCastUiScreen)
                    || typeName == nameof(AunCastAudioOutputTunnel);
            }
        }

        public override void OnInspectorGUI()
        {
            if (ShouldDrawBanner)
                AunCastInspectorBanner.Draw(this);
            if (UdonSharpGUI.DrawProgramSource(target, false)) return;

            serializedObject.Update();
            AunCastManagedSettingsInspectorUtility.DrawPropertiesWithManagedFoldouts(
                serializedObject,
                new[] { "m_Script" },
                new[]
                {
                    new AunCastManagedSettingsInspectorUtility.ManagedPropertyGroup(
                        "共通設定（AunCastSettings 管理下）",
                        "Common Settings (Managed by AunCastSettings)",
                        SettingsManagedPropertyNames),
                    new AunCastManagedSettingsInspectorUtility.ManagedPropertyGroup(
                        "テーマ設定（AunCastThemeApplier 管理下）",
                        "Theme Settings (Managed by AunCastThemeApplier)",
                        ThemeApplierManagedPropertyNames),
                    new AunCastManagedSettingsInspectorUtility.ManagedPropertyGroup(
                        "配線対象（変更不可）",
                        "Wiring References (Read Only)",
                        WiringPropertyNames),
                },
                _showManagedPropertyGroups);
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(AunCastDualPlayerController))]
    internal sealed class AunCastDualPlayerControllerInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "defaultVolume",
            "defaultUrl",
            "_autoSilenceResyncEnabled",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "playerManagerA",
            "playerManagerB",
            "playbackMonitor",
            "switcher",
            "activeMonitor",
            "resyncClient",
            "eventBus",
            "staffNotifyTarget",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastActivePlayerMonitor))]
    internal sealed class AunCastActivePlayerMonitorInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] MANAGED_PROPERTY_NAMES =
        {
            "monitorIntervalSec",
            "minAdvanceThresholdSec",
            "minConsecutiveAdvances",
            "stalledTimeoutSec",
            "driftSmoothingTimeConstant",
            "driftWarmupSec",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "playerManagerA",
            "playerManagerB",
        };

        protected override string[] SettingsManagedPropertyNames => MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastResyncCoordinatorClient))]
    internal sealed class AunCastResyncCoordinatorClientInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] MANAGED_PROPERTY_NAMES =
        {
            "resyncCycleTimeoutSec",
            "silenceSuppressSec",
            "localCooldownSec",
            "baseCooldownSec",
            "retryCooldownMultiplier",
            "maxRetryCooldownSec",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "coordinator",
        };

        protected override string[] SettingsManagedPropertyNames => MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastResyncCoordinator))]
    internal sealed class AunCastResyncCoordinatorInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "grantTimeoutSec",
            "runningTimeoutSec",
            "maxConcurrentResyncUsers",
            "maxConnectionLimit",
            "driftResyncThresholdIndex",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "playbackMonitor",
            "staffNotifyTarget",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastPlaybackMonitor))]
    internal sealed class AunCastPlaybackMonitorInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "staffNotifyTarget",
            "coordinator",
        };

        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastStaffControlPanel))]
    internal sealed class AunCastStaffControlPanelInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] MANAGED_PROPERTY_NAMES =
        {
            "allowedUserNames",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "controller",
            "coordinator",
            "viewerStatusPanel",
            "nowPlayingText",
            "nextUrlField",
            "nextUrlFieldPlaceholderText",
            "stopButton",
            "globalResyncButton",
            "forceRebootButton",
            "promoteNextButton",
            "helpTextField",
            "indicatorText",
            "userCountText",
            "concurrentLimitDisplayText",
            "concurrentDisplayGroup",
            "concurrentEditGroup",
            "concurrentLimitInput",
            "connectionLimitDisplayText",
            "connectionDisplayGroup",
            "connectionEditGroup",
            "connectionLimitInput",
            "driftThresholdDisplayText",
            "driftThresholdDisplayGroup",
            "driftThresholdEditGroup",
            "driftThresholdEditValueText",
            "forceModeDisplayText",
            "forceModeDisplayGroup",
            "forceModeEditGroup",
            "forceModeEditValueText",
            "forceModeChangeButton",
            "forceModePreviousButton",
            "forceModeNextButton",
            "forceModeApplyButton",
            "forceModeCancelButton",
        };

        protected override string[] SettingsManagedPropertyNames => MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastPortablePanel))]
    internal sealed class AunCastPortablePanelInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "silenceMeterPeakHoldSec",
            "silenceMeterPeakDecayDbPerSec",
            "summonGesture",
            "vrBothTriggersHoldSec",
            "vrRightStickUpHoldSec",
            "desktopEscHoldSec",
            "desktopSummonGesture",
            "autoDismissDistance",
            "outOfSightDismissSec",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "controller",
            "coordinator",
            "eventBus",
            "stateText",
            "headroomGauge",
            "silenceGauge",
            "silenceThresholdMarker",
            "silencePeakMarker",
            "volumeSlider",
            "autoSilenceResyncToggle",
            "modeDisplayGroup",
            "modeDisplayText",
            "modeEditButton",
            "modeEditGroup",
            "modeEditValueText",
            "modePreviousButton",
            "modeNextButton",
            "modeApplyButton",
            "modeCancelButton",
            "timelineLoggingToggle",
            "staffLockButton",
            "staffLockButtonLabel",
            "resyncButton",
            "rebootButton",
            "closeButton",
            "backgroundImage",
            "contentCanvasGroup",
            "userContentCanvasGroup",
            "staffContentCanvasGroup",
            "staffControlPanel",
            "switchButton",
            "hudProgress",
        };

        private static readonly string[] THEME_APPLIER_MANAGED_PROPERTY_NAMES =
        {
            "userBackgroundColor",
            "staffBackgroundColor",
            "disabledButtonLabelAlpha",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] ThemeApplierManagedPropertyNames => THEME_APPLIER_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastHudProgressOverlay))]
    internal sealed class AunCastHudProgressOverlayInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "desktopLocalOffset",
            "showThreshold",
        };

        private static readonly string[] THEME_APPLIER_MANAGED_PROPERTY_NAMES =
        {
            "vrLocalOffset",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "quadTransform",
            "quadRenderer",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] ThemeApplierManagedPropertyNames => THEME_APPLIER_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastSpeaker))]
    internal sealed class AunCastSpeakerInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] MANAGED_PROPERTY_NAMES =
        {
            "silenceRmsThresholdDbfs",
            "silenceConsecutiveSec",
        };

        protected override string[] SettingsManagedPropertyNames => MANAGED_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastScreen))]
    internal sealed class AunCastScreenInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "idleTexture",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "eventBus",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastUiScreen))]
    internal sealed class AunCastUiScreenInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] SETTINGS_MANAGED_PROPERTY_NAMES =
        {
            "idleTexture",
        };

        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "eventBus",
        };

        protected override string[] SettingsManagedPropertyNames => SETTINGS_MANAGED_PROPERTY_NAMES;
        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastEventBus))]
    internal sealed class AunCastEventBusInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "videoTextureSubscribers",
            "localStateSubscribers",
            "portablePanelShownSubscribers",
        };

        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }

    [CustomEditor(typeof(AunCastAudioOutputTunnel))]
    internal sealed class AunCastAudioOutputTunnelInspector : AunCastManagedSettingsInspectorBase
    {
        private static readonly string[] WIRING_PROPERTY_NAMES =
        {
            "inputA",
            "inputB",
            "targetTunnel",
        };

        protected override string[] WiringPropertyNames => WIRING_PROPERTY_NAMES;
    }
}
#endif
