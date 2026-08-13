// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Reflection;
using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Enumerations;
using Microsoft.PowerToys.Settings.UI.UnitTests.Mocks;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ViewModelTests
{
    [TestClass]
    public class ClipPing
    {
        private Mock<SettingsUtils> _generalSettingsUtils;
        private Mock<SettingsUtils> _clipPingSettingsUtils;

        [TestInitialize]
        public void SetUpStubSettingUtils()
        {
            _generalSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<GeneralSettings>();
            _clipPingSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<ClipPingSettings>();
        }

        [TestMethod]
        public void IsEnabledWhenChangedShouldSendGeneralSettings()
        {
            var settingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            var ipcInvoked = false;
            var expectedEnabled = false;
            var viewModel = CreateViewModel(
                settingsUtils,
                message =>
                {
                    var outgoing = JsonSerializer.Deserialize(
                        message,
                        SettingsSerializationContext.Default.OutGoingGeneralSettings);
                    Assert.IsNotNull(outgoing);
                    Assert.AreEqual(expectedEnabled, outgoing.GeneralSettings.Enabled.ClipPing);
                    ipcInvoked = true;
                    return 0;
                });

            expectedEnabled = !viewModel.IsEnabled;
            viewModel.IsEnabled = expectedEnabled;

            Assert.IsTrue(ipcInvoked);
        }

        [TestMethod]
        public void GpoNotConfiguredShouldAllowUserControl()
        {
            var viewModel = CreateViewModel(new Mock<SettingsUtils>(new FileSystem(), null));

            Assert.IsFalse(viewModel.IsEnabledGpoConfigured);
        }

        [TestMethod]
        public void RefreshEnabledStateShouldClearRemovedPolicyAndNotifyBindings()
        {
            var viewModel = CreateViewModel(new Mock<SettingsUtils>(new FileSystem(), null));
            var policyField = typeof(ClipPingViewModel).GetField(
                "_enabledStateIsGpoConfigured",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(policyField);
            policyField.SetValue(viewModel, true);

            var changedProperties = new List<string>();
            viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            viewModel.RefreshEnabledState();

            Assert.IsFalse(viewModel.IsEnabledGpoConfigured);
            CollectionAssert.Contains(changedProperties, nameof(ClipPingViewModel.IsEnabled));
            CollectionAssert.Contains(changedProperties, nameof(ClipPingViewModel.IsEnabledGpoConfigured));
        }

        [TestMethod]
        public void OverlayColorWhenChangedShouldPersistSettings()
        {
            var settingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            var viewModel = CreateViewModel(settingsUtils);

            viewModel.OverlayColor = "#12ABEF";

            Assert.AreEqual("#12ABEF", viewModel.OverlayColor);
            settingsUtils.Verify(
                x => x.SaveSettings(
                    It.Is<string>(json => json.Contains("#12ABEF", StringComparison.Ordinal)),
                    ClipPingSettings.ModuleName,
                    It.IsAny<string>()),
                Times.AtLeastOnce);
        }

        [TestMethod]
        public void OverlayColorWhenNullShouldUseSharedDefault()
        {
            var settingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            var viewModel = CreateViewModel(settingsUtils);

            viewModel.OverlayColor = null;

            Assert.AreEqual(ClipPingProperties.DefaultOverlayColor, viewModel.OverlayColor);
        }

        [TestMethod]
        public void OverlayTypeWhenChangedShouldPersistSettings()
        {
            var settingsUtils = new Mock<SettingsUtils>(new FileSystem(), null);
            var viewModel = CreateViewModel(settingsUtils);

            viewModel.OverlayType = (int)ClipPingOverlay.Border;

            Assert.AreEqual((int)ClipPingOverlay.Border, viewModel.OverlayType);
            settingsUtils.Verify(
                x => x.SaveSettings(
                    It.Is<string>(json => json.Contains("\"OverlayType\":1", StringComparison.Ordinal)),
                    ClipPingSettings.ModuleName,
                    It.IsAny<string>()),
                Times.AtLeastOnce);
        }

        private ClipPingViewModel CreateViewModel(Mock<SettingsUtils> settingsUtils, Func<string, int> ipcCallback = null)
        {
            return new ClipPingViewModel(
                settingsUtils.Object,
                SettingsRepository<GeneralSettings>.GetInstance(_generalSettingsUtils.Object),
                SettingsRepository<ClipPingSettings>.GetInstance(_clipPingSettingsUtils.Object),
                ipcCallback ?? (_ => 0));
        }
    }
}
