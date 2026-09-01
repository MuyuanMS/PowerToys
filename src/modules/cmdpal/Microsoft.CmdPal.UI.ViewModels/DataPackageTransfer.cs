// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ManagedCommon;
using Windows.ApplicationModel.DataTransfer;

namespace Microsoft.CmdPal.UI.ViewModels;

public static class DataPackageTransfer
{
    public static async Task CopyAsync(DataPackageView source, DataPackage destination)
    {
        destination.RequestedOperation = source.RequestedOperation;

        foreach (var (key, value) in source.Properties)
        {
            try
            {
                destination.Properties[key] = value;
            }
            catch (Exception)
            {
                // Skip properties that cannot be copied into the drag data package.
            }
        }

        var resourceMapTask = CopyResourceMapAsync(source, destination);

        foreach (var format in source.AvailableFormats)
        {
            try
            {
                destination.SetDataProvider(format, request => DelayRenderer(request, source, format, resourceMapTask));
            }
            catch (Exception)
            {
                // Skip formats that cannot be registered on the drag data package.
            }
        }

        await resourceMapTask;
    }

    private static async Task CopyResourceMapAsync(DataPackageView source, DataPackage destination)
    {
        try
        {
            var resourceMap = await source.GetResourceMapAsync();
            foreach (var (key, value) in resourceMap)
            {
                destination.ResourceMap[key] = value;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to copy the resource map during drag-and-drop", ex);
        }
    }

    private static async void DelayRenderer(
        DataProviderRequest request,
        DataPackageView source,
        string format,
        Task resourceMapTask)
    {
        var deferral = request.GetDeferral();
        try
        {
            await resourceMapTask;
            request.SetData(await source.GetDataAsync(format));
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to set data for format '{format}' during drag-and-drop", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
