// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using ManagedCommon;
using Microsoft.PowerToys.Telemetry;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Peek.Common.Extensions;
using Peek.Common.Helpers;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers.Helpers;
using Peek.FilePreviewer.Previewers.Interfaces;
using Peek.UI.Telemetry.Events;
using Windows.Foundation;

namespace Peek.FilePreviewer.Previewers
{
    public partial class UnsupportedFilePreviewer : ObservableObject, IUnsupportedFilePreviewer, IReusablePreviewer
    {
        /// <summary>
        /// The number of files to scan between updates when calculating folder size.
        /// </summary>
        private const int FolderEnumerationChunkSize = 100;

        /// <summary>
        /// The maximum view updates per second when enumerating a folder's contents.
        /// </summary>
        private const int MaxUpdateFps = 15;

        /// <summary>
        /// The icon to display when a file or folder's thumbnail or icon could not be retrieved.
        /// </summary>
        private static readonly SvgImageSource DefaultIcon = new(new Uri("ms-appx:///Assets/Peek/DefaultFileIcon.svg"));

        /// <summary>
        /// The options to use for the folder size enumeration. We recurse through all files and all subfolders.
        /// </summary>
        private static readonly EnumerationOptions FolderEnumerationOptions;

        [ObservableProperty]
        private UnsupportedFilePreviewData preview = new();

        [ObservableProperty]
        private PreviewState state;

        private int _bindingGeneration;

        static UnsupportedFilePreviewer()
        {
            FolderEnumerationOptions = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        }

        public UnsupportedFilePreviewer(IFileSystemItem file)
        {
            Item = file;
            Dispatcher = DispatcherQueue.GetForCurrentThread();
            Preview = CreatePreviewData(file);
        }

        public IFileSystemItem Item { get; private set; }

        public void Rebind(IFileSystemItem item, double scalingFactor)
        {
            Item = item;
            Interlocked.Increment(ref _bindingGeneration);
            Preview = CreatePreviewData(item);
            State = PreviewState.Loading;
        }

        private DispatcherQueue Dispatcher { get; }

        private int BindingGeneration => Volatile.Read(ref _bindingGeneration);

        public Task<PreviewSize> GetPreviewSizeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewSize { MonitorSize = new Size(680, 500), UseEffectivePixels = true });

        public async Task LoadPreviewAsync(CancellationToken cancellationToken)
        {
            var bindingGeneration = BindingGeneration;
            var item = Item;
            var previewData = Preview;

            try
            {
                ThrowIfStale(bindingGeneration, cancellationToken);

                if (item is not FolderItem)
                {
                    PowerToysTelemetry.Log.WriteEvent(
                        new ErrorEvent() { Failure = ErrorEvent.FailureType.FileNotSupported });
                }

                await Dispatcher.RunOnUiThread(async () =>
                {
                    ThrowIfStale(bindingGeneration, cancellationToken);
                    State = PreviewState.Loaded;
                    await LoadIconPreviewAsync(item, previewData, bindingGeneration, cancellationToken);
                });

                var progress = new Progress<string>(update =>
                {
                    EnqueueIfCurrent(bindingGeneration, () => previewData.FileSize = update);
                });

                await LoadDisplayInfoAsync(item, previewData, progress, bindingGeneration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogError("UnsupportedFilePreviewer error.", ex);
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

        private async Task LoadIconPreviewAsync(IFileSystemItem item, UnsupportedFilePreviewData previewData, int bindingGeneration, CancellationToken cancellationToken)
        {
            var iconPreview = await ThumbnailHelper.GetThumbnailAsync(item.Path, cancellationToken) ??
                await ThumbnailHelper.GetIconAsync(item.Path, cancellationToken) ??
                DefaultIcon;

            ThrowIfStale(bindingGeneration, cancellationToken);
            previewData.IconPreview = iconPreview;
        }

        private async Task LoadDisplayInfoAsync(IFileSystemItem item, UnsupportedFilePreviewData previewData, IProgress<string> sizeProgress, int bindingGeneration, CancellationToken cancellationToken)
        {
            string type = await item.GetContentTypeAsync();

            ThrowIfStale(bindingGeneration, cancellationToken);
            EnqueueIfCurrent(bindingGeneration, () => previewData.FileType = type);

            if (item is FolderItem)
            {
                await Task.Run(() => CalculateFolderSizeWithProgress(item.Path, sizeProgress, cancellationToken), cancellationToken);
            }
            else
            {
                ThrowIfStale(bindingGeneration, cancellationToken);
                ReportProgress(sizeProgress, item.FileSizeBytes);
            }
        }

        private static UnsupportedFilePreviewData CreatePreviewData(IFileSystemItem item)
        {
            return new UnsupportedFilePreviewData
            {
                FileName = item.Name,
                DateModified = item.DateModified?.ToString(CultureInfo.CurrentCulture),
                IconPreview = DefaultIcon,
            };
        }

        private void ThrowIfStale(int bindingGeneration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (bindingGeneration != BindingGeneration)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private void EnqueueIfCurrent(int bindingGeneration, Action action)
        {
            if (bindingGeneration != BindingGeneration)
            {
                return;
            }

            Dispatcher.TryEnqueue(() =>
            {
                if (bindingGeneration == BindingGeneration)
                {
                    action();
                }
            });
        }

        private void CalculateFolderSizeWithProgress(string path, IProgress<string> progress, CancellationToken cancellationToken)
        {
            ulong folderSize = 0;
            TimeSpan updateInterval = TimeSpan.FromMilliseconds(1000 / MaxUpdateFps);
            DateTime nextUpdate = DateTime.UtcNow + updateInterval;

            var files = new DirectoryInfo(path).EnumerateFiles("*", FolderEnumerationOptions);

            foreach (var chunk in files.Chunk(FolderEnumerationChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DateTime.Now >= nextUpdate)
                {
                    ReportProgress(progress, folderSize);
                    nextUpdate = DateTime.UtcNow + updateInterval;
                }

                foreach (var file in chunk)
                {
                    folderSize += (ulong)file.Length;
                }
            }

            ReportProgress(progress, folderSize);
        }

        private void ReportProgress(IProgress<string> progress, ulong size)
        {
            progress.Report(ReadableStringHelper.BytesToReadableString(size));
        }
    }
}
