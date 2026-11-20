// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AdvancedPaste.Cli;

internal static class AzureVisionOcrSetup
{
    internal static void Show()
    {
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            using var form = new Form
            {
                Text = "PowerScripts - Azure Computer Vision OCR setup",
                ClientSize = new Size(640, 380),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Font = new Font("Segoe UI", 10),
                AutoScaleMode = AutoScaleMode.Dpi,
            };
            var explanation = new Label
            {
                Text = "Use Azure Computer Vision for the image-to-text CLI action.\r\nGet these values from Azure Portal > your Computer Vision resource > Keys and Endpoint.",
                Location = new Point(24, 20),
                Size = new Size(592, 56),
            };
            var endpointLabel = new Label { Text = "Endpoint", Location = new Point(24, 88), AutoSize = true };
            var endpoint = new TextBox
            {
                Name = "AzureVisionEndpoint",
                AccessibleName = "Azure Computer Vision endpoint",
                PlaceholderText = "https://your-resource.cognitiveservices.azure.com/",
                Location = new Point(24, 112),
                Width = 592,
            };
            var keyLabel = new Label { Text = "API key (Key 1 or Key 2)", Location = new Point(24, 156), AutoSize = true };
            var key = new TextBox
            {
                Name = "AzureVisionApiKey",
                AccessibleName = "Azure Computer Vision API key",
                UseSystemPasswordChar = true,
                Location = new Point(24, 180),
                Width = 592,
            };
            var notice = new Label
            {
                Text = "The key is stored in your Windows Credential Vault, not in scripts or demo files.\r\nAfter saving, OCR uploads selected images to your Azure resource; charges may apply.\r\nSave does not upload an image. Sandbox protections stay enabled.",
                Location = new Point(24, 228),
                Size = new Size(592, 64),
            };
            var offline = new Button { Text = "Use offline Windows OCR", Location = new Point(24, 320), Size = new Size(216, 36) };
            var save = new Button { Text = "Save Azure profile", Location = new Point(432, 320), Size = new Size(184, 36) };
            save.Click += (_, _) =>
            {
                try
                {
                    AzureVisionOcr.SaveProfile(endpoint.Text, key.Text);
                    key.Clear();
                    MessageBox.Show(form, "Saved securely. Run Extract text from image from Explorer to use Azure OCR.", "Azure OCR ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    form.Close();
                }
                catch (Exception exception)
                {
                    var message = exception is ArgumentException ? exception.Message : "Unable to save the profile in Windows Credential Vault. No image was uploaded.";
                    MessageBox.Show(form, message, "Setup not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            offline.Click += (_, _) =>
            {
                try
                {
                    AzureVisionOcr.RemoveProfile();
                    key.Clear();
                    form.Close();
                }
                catch (Exception)
                {
                    MessageBox.Show(form, "Unable to remove the Azure OCR profile.", "Setup unchanged", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            form.FormClosed += (_, _) => key.Clear();
            form.Controls.AddRange([explanation, endpointLabel, endpoint, keyLabel, key, notice, offline, save]);
            form.AcceptButton = save;
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
