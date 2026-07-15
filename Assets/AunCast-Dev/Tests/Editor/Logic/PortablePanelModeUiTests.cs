using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PasocomMate.AunCast.Tests
{
    public class PortablePanelModeUiTests
    {
        private AunCastPortablePanel _panel;
        private AunCastDualPlayerController _controller;
        private Button _modeEditButton;
        private TMP_Text _modeEditButtonLabel;

        [SetUp]
        public void SetUp()
        {
            _panel = TestHelper.CreateComponent<AunCastPortablePanel>();
            _controller = TestHelper.CreateComponent<AunCastDualPlayerController>();

            var buttonGo = new GameObject("ModeEditButton");
            _modeEditButton = buttonGo.AddComponent<Button>();

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(buttonGo.transform, false);
            _modeEditButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _modeEditButtonLabel.color = Color.white;

            TestHelper.Set(_panel, "controller", _controller);
            TestHelper.Set(_panel, "modeEditButton", _modeEditButton);
            TestHelper.Set(_panel, "disabledButtonLabelAlpha", 0.25f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_modeEditButton != null)
                Object.DestroyImmediate(_modeEditButton.gameObject);
            TestHelper.Destroy(_controller);
            TestHelper.Destroy(_panel);
        }

        [Test]
        public void SyncModeUI_ForceModeDisablesModeEditButtonAndDimsLabel()
        {
            TestHelper.Set(
                _controller,
                "_syncedForceMode",
                AunCastDualPlayerController.FORCE_MODE_MANUAL);

            TestHelper.Invoke(_panel, "SyncModeUI");

            Assert.IsFalse(_modeEditButton.interactable,
                "Force Mode 中は Mode の Edit ボタンを操作不可にするべき");
            Assert.AreEqual(0.25f, _modeEditButtonLabel.color.a, 0.0001f,
                "Mode の Edit ボタン無効時はラベル alpha も disabledButtonLabelAlpha に揃えるべき");
        }

        [Test]
        public void SyncModeUI_NoForceModeEnablesModeEditButtonAndRestoresLabel()
        {
            _modeEditButton.interactable = false;
            _modeEditButtonLabel.color = new Color(1f, 1f, 1f, 0.25f);

            TestHelper.Set(
                _controller,
                "_syncedForceMode",
                AunCastDualPlayerController.FORCE_MODE_NONE);

            TestHelper.Invoke(_panel, "SyncModeUI");

            Assert.IsTrue(_modeEditButton.interactable,
                "Force Mode なしでは Mode の Edit ボタンを操作可能にするべき");
            Assert.AreEqual(1f, _modeEditButtonLabel.color.a, 0.0001f,
                "Mode の Edit ボタン有効時はラベル alpha を戻すべき");
        }
    }
}
