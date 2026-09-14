// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using AdaptiveCards.ObjectModel.WinUI3;
using AdaptiveCards.Rendering.WinUI3;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.CmdPal.AdaptiveCards.IncrementalRendering.UnitTests;

[TestClass]
public sealed class IncrementalAdaptiveCardUpdaterTests
{
    [TestMethod]
    public async Task UpdateAsync_RendersCardAndRetainsCardState()
    {
        await RunOnDedicatedDispatcherAsync(async () =>
        {
            var host = new Border();
            var updater = new IncrementalAdaptiveCardUpdater(
                new AdaptiveCardRenderer(),
                host);
            var card = ParseCard("""{"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":"hello"}]}""");

            await updater.UpdateAsync(card);

            Assert.AreSame(card, updater.Card);
            Assert.IsNotNull(updater.RenderedCard);
            Assert.IsNotNull(host.Child);
        });
    }

    [TestMethod]
    public async Task Reset_ClearsRenderedCardAndHost()
    {
        await RunOnDedicatedDispatcherAsync(async () =>
        {
            var host = new Border();
            var updater = new IncrementalAdaptiveCardUpdater(
                new AdaptiveCardRenderer(),
                host);
            var card = ParseCard("""{"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":"hello"}]}""");

            await updater.UpdateAsync(card);
            updater.Reset();

            Assert.IsNull(updater.Card);
            Assert.IsNull(updater.RenderedCard);
            Assert.IsNull(host.Child);
        });
    }

    private static AdaptiveCard ParseCard(string json)
    {
        return AdaptiveCard.FromJsonString(json).AdaptiveCard;
    }

    private static async Task RunOnDedicatedDispatcherAsync(Func<Task> action)
    {
        DispatcherQueueController controller;
        try
        {
            controller = DispatcherQueueController.CreateOnDedicatedThread();
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80040154)
        {
            Assert.Inconclusive(
                "WinUI 3 runtime registration is required to run dispatcher-backed tests.");
            return;
        }

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        controller.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                await controller.ShutdownQueueAsync();
            }
        });

        await completion.Task;
    }
}
