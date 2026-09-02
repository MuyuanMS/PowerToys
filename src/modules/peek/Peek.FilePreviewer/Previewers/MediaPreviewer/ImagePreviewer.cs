// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.PowerToys.FilePreviewCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Peek.Common.Extensions;
using Peek.Common.Helpers;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers.Helpers;
using Peek.FilePreviewer.Previewers.Interfaces;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Peek.FilePreviewer.Previewers
{
    public partial class ImagePreviewer : ObservableObject, IImagePreviewer, IReusablePreviewer
    {
        [ObservableProperty]
        private ImageSource? preview;

        [ObservableProperty]
        private PreviewState state;

        [ObservableProperty]
        private Size? imageSize;

        [ObservableProperty]
        private Size maxImageSize;

        [ObservableProperty]
        private double scalingFactor;

        public ImagePreviewer(IFileSystemItem file)
        {
            Item = file;
            Dispatcher = DispatcherQueue.GetForCurrentThread();
        }

        public IFileSystemItem Item { get; private set; }

        public void Rebind(IFileSystemItem item, double scalingFactor)
        {
            Item = item;
            ScalingFactor = scalingFactor;
            State = PreviewState.Loading;
        }

        private static bool IsPng(IFileSystemItem item) => item.Extension == ".png";

        private static bool IsQoi(IFileSystemItem item) => item.Extension == ".qoi";

        private DispatcherQueue Dispatcher { get; }

        private static readonly HashSet<string> _supportedFileTypes =
            BitmapDecoder.GetDecoderInformationEnumerator()
                .SelectMany(di => di.FileExtensions)
                .Union([".qoi"])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static bool IsItemSupported(IFileSystemItem item)
        {
            return _supportedFileTypes.Contains(item.Extension);
        }

        public async Task<PreviewSize> GetPreviewSizeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = Item;
            Size? size;
            if (IsQoi(item))
            {
                size = await Task.Run(item.GetQoiSize);
            }
            else
            {
                size = await Task.Run(item.GetImageSize)
                    ?? await WICHelper.GetImageSize(item.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // If an image is already loaded (e.g. scaling factor changed on the current item),
            // update ImageSize immediately so MaxImageSize matches the new DPI scale.
            if (State == PreviewState.Loaded)
            {
                ImageSize = size;
            }

            return new PreviewSize { MonitorSize = size };
        }

        public async Task LoadPreviewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            State = PreviewState.Loading;
            var item = Item;

            bool loaded = await LoadFullQualityImageAsync(item, cancellationToken);

            if (!loaded)
            {
                loaded = await LoadThumbnailAsync(item, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (loaded)
            {
                State = PreviewState.Loaded;
            }
            else
            {
                State = PreviewState.Error;
            }
        }

        public async Task CopyAsync()
        {
            await Dispatcher.RunOnUiThread(async () =>
            {
                var storageItem = await Item.GetStorageItemAsync();
                ClipboardHelper.SaveToClipboard(storageItem);
            });
        }

        partial void OnScalingFactorChanged(double value)
        {
            UpdateMaxImageSize();
        }

        partial void OnImageSizeChanged(Size? value)
        {
            UpdateMaxImageSize();
        }

        private void UpdateMaxImageSize()
        {
            double imageWidth = ImageSize?.Width ?? 0;
            double imageHeight = ImageSize?.Height ?? 0;

            MaxImageSize = ScalingFactor != 0 ?
                new Size(imageWidth / ScalingFactor, imageHeight / ScalingFactor) :
                new Size(imageWidth, imageHeight);
        }

        private Task<bool> LoadThumbnailAsync(IFileSystemItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return TaskExtension.RunSafe(async () =>
            {
                await Dispatcher.RunOnUiThread(async () =>
                {
                    var thumbnail = await ThumbnailHelper.GetCachedThumbnailAsync(item.Path, IsPng(item), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Preview = thumbnail;
                });
            });
        }

        private Task<bool> LoadFullQualityImageAsync(IFileSystemItem item, CancellationToken cancellationToken)
        {
            return TaskExtension.RunSafe(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Dispatcher.RunOnUiThread(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsQoi(item))
                    {
                        using FileStream stream = ReadHelper.OpenReadOnly(item.Path);
                        using var bitmap = QoiImage.FromStream(stream);

                        var source = await BitmapHelper.BitmapToImageSource(bitmap, true, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        Preview = source;
                    }
                    else
                    {
                        using FileStream stream = ReadHelper.OpenReadOnly(item.Path);
                        var bmp = new BitmapImage();

                        await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                        cancellationToken.ThrowIfCancellationRequested();
                        Preview = bmp;
                    }
                });
            });
        }
    }
}
