// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ManagedCommon;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Microsoft.CmdPal.UI.ViewModels;

public static class DataPackageTransfer
{
    public static async Task CopyAsync(DataPackageView source, DataPackage destination)
    {
        var resourceMap = await PrepareResourceMapAsync(source);
        Copy(source, destination, resourceMap);
    }

    public static async Task<IReadOnlyDictionary<string, RandomAccessStreamReference>?> PrepareResourceMapAsync(DataPackageView source)
    {
        try
        {
            return await source.GetResourceMapAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to get the resource map during drag-and-drop", ex);
            return null;
        }
    }

    public static bool TryCopy(
        DataPackageView source,
        DataPackage destination,
        Task<IReadOnlyDictionary<string, RandomAccessStreamReference>?>? resourceMapTask)
    {
        if (resourceMapTask?.IsCompletedSuccessfully != true)
        {
            return false;
        }

        Copy(source, destination, resourceMapTask.Result);
        return true;
    }

    private static void Copy(
        DataPackageView source,
        DataPackage destination,
        IReadOnlyDictionary<string, RandomAccessStreamReference>? resourceMap)
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

        if (resourceMap is not null)
        {
            foreach (var (key, value) in resourceMap)
            {
                destination.ResourceMap[key] = value;
            }
        }

        foreach (var format in source.AvailableFormats)
        {
            try
            {
                destination.SetDataProvider(format, request => DelayRenderer(request, source, format));
            }
            catch (Exception)
            {
                // Skip formats that cannot be registered on the drag data package.
            }
        }
    }

    private static async void DelayRenderer(DataProviderRequest request, DataPackageView source, string format)
    {
        var deferral = request.GetDeferral();
        try
        {
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
