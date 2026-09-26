// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Peek.Common.Constants;
using Peek.Common.Extensions;
using Peek.Common.Helpers;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers.Interfaces;

namespace Peek.FilePreviewer.Previewers
{
    public partial class WebBrowserPreviewer : ObservableObject, IBrowserPreviewer, IDisposable
    {
        private readonly IPreviewSettings _previewSettings;

        private static readonly HashSet<string> _supportedFileTypes = new()
        {
            // Web
            ".html",
            ".htm",

            // Document
            ".pdf",

            // Markdown
            ".md",

            // SVG - using WebView2 for better compatibility with complex SVGs
            // (e.g., from Adobe Illustrator, Inkscape)
            ".svg",
        };

        [ObservableProperty]
        private Uri? preview;

        [ObservableProperty]
        private PreviewState state;

        [ObservableProperty]
        private bool isDevFilePreview;

        [ObservableProperty]
        private bool customContextMenu;

        private bool disposed;

        public WebBrowserPreviewer(IFileSystemItem file, IPreviewSettings previewSettings)
        {
            _previewSettings = previewSettings;
            File = file;
            Dispatcher = DispatcherQueue.GetForCurrentThread();
        }

        internal WebBrowserPreviewer(IFileSystemItem file, IPreviewSettings previewSettings, DispatcherQueue? dispatcher)
        {
            _previewSettings = previewSettings;
            File = file;
            Dispatcher = dispatcher;
        }

        ~WebBrowserPreviewer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual async void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                await Microsoft.PowerToys.FilePreviewCommon.Helper.CleanupTempDirAsync(TempFolderPath.Path);
                disposed = true;
            }
        }

        private IFileSystemItem File { get; }

        public bool IsPreviewLoaded => Preview != null;

        private DispatcherQueue? Dispatcher { get; }

        private Task<bool>? DisplayInfoTask { get; set; }

        public Task<PreviewSize> GetPreviewSizeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PreviewSize { MonitorSize = null });
        }

        public async Task LoadPreviewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = PreviewState.Loading;
            DisplayInfoTask = LoadDisplayInfoAsync(cancellationToken);
            await DisplayInfoTask; // Wait for the display info to load before checking for errors

            if (HasFailedLoadingPreview())
            {
                State = PreviewState.Error;
            }
        }

        public Task<bool> LoadDisplayInfoAsync(CancellationToken cancellationToken)
        {
            return TaskExtension.RunSafe(async () =>
            {
                if (Dispatcher == null)
                {
                    throw new InvalidOperationException("A dispatcher is required to load the preview.");
                }

                var previewContent = await CreatePreviewAsync(cancellationToken).ConfigureAwait(false);

                await Dispatcher.RunOnUiThread(() =>
                {
                    IsDevFilePreview = previewContent.IsDevFilePreview;
                    CustomContextMenu = previewContent.CustomContextMenu;
                    Preview = previewContent.PreviewUri;
                });
            });
        }

        internal async Task<(Uri PreviewUri, bool IsDevFilePreview, bool CustomContextMenu)> CreatePreviewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string extension = File.Extension;

            if (extension == ".md")
            {
                var raw = await ReadHelper.Read(File.Path.ToString(), _previewSettings.SourceCodeMaxFileSizeBytes, cancellationToken).ConfigureAwait(false);
                return (new Uri(MarkdownHelper.PreviewTempFile(raw, File.Path, TempFolderPath.Path)), false, false);
            }

            if (extension == ".svg" || extension == ".html" || extension == ".htm" || extension == ".pdf")
            {
                return (new Uri(File.Path), false, false);
            }

            // Source code files use Monaco editor. Only extensions Monaco doesn't recognize are sniffed
            // to check whether they can be displayed as plain text.
            if (!IsItemSupported(File) && !await TextFileHelper.IsTextFileAsync(File.Path, _previewSettings.SourceCodeMaxFileSizeBytes, cancellationToken).ConfigureAwait(false))
            {
                throw new NotSupportedException($"'{File.Path}' has an unrecognized extension and its content was not sniffed as text.");
            }

            var content = await ReadHelper.Read(File.Path.ToString(), _previewSettings.SourceCodeMaxFileSizeBytes, cancellationToken).ConfigureAwait(false);
            var previewUri = new Uri(MonacoHelper.PreviewTempFile(content, extension, TempFolderPath.Path, _previewSettings.SourceCodeTryFormat, _previewSettings.SourceCodeWrapText, _previewSettings.SourceCodeStickyScroll, _previewSettings.SourceCodeFontSize, _previewSettings.SourceCodeMinimap));
            return (previewUri, true, true);
        }

        public async Task CopyAsync()
        {
            if (Dispatcher == null)
            {
                throw new InvalidOperationException("A dispatcher is required to copy the previewed file.");
            }

            await Dispatcher.RunOnUiThread(async () =>
            {
                var storageItem = await File.GetStorageItemAsync();
                ClipboardHelper.SaveToClipboard(storageItem);
            });
        }

        public static bool IsItemSupported(IFileSystemItem item)
        {
            return _supportedFileTypes.Contains(item.Extension) || MonacoHelper.SupportedMonacoFileTypes.Contains(item.Extension);
        }

        /// <summary>
        /// Last-resort check for files with no extension, or an extension Peek doesn't otherwise
        /// recognize, whose content should be checked to confirm if it is text. Should only be
        /// consulted after every other previewer (including the shell preview handler)
        /// has declined the item.
        /// </summary>
        public static bool IsFallbackCandidate(IFileSystemItem item)
        {
            return item is FileItem && !IsItemSupported(item);
        }

        private bool HasFailedLoadingPreview()
        {
            return !(DisplayInfoTask?.Result ?? true);
        }
    }
}
