// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.PowerToys.Telemetry;
using Microsoft.UI.Xaml;
using Peek.Common.Extensions;
using Peek.Common.Models;
using Peek.FilePreviewer.Models;
using Peek.FilePreviewer.Previewers.Archives;
using Peek.FilePreviewer.Previewers.Drive;
using Peek.FilePreviewer.Previewers.MediaPreviewer;
using Peek.UI.Telemetry.Events;
using SqliteNS = Peek.FilePreviewer.Previewers.SqlitePreviewer;

namespace Peek.FilePreviewer.Previewers
{
    public class PreviewerFactory
    {
        private readonly IPreviewSettings _previewSettings;
        private readonly bool _useCurrentDispatcher;

        public PreviewerFactory()
        {
            _previewSettings = Application.Current.GetService<IPreviewSettings>();
            _useCurrentDispatcher = true;
        }

        internal PreviewerFactory(IPreviewSettings previewSettings, bool useCurrentDispatcher = true)
        {
            _previewSettings = previewSettings;
            _useCurrentDispatcher = useCurrentDispatcher;
        }

        public IPreviewer Create(IFileSystemItem item)
        {
            if (ImagePreviewer.IsItemSupported(item))
            {
                return new ImagePreviewer(item);
            }
            else if (VideoPreviewer.IsItemSupported(item))
            {
                return new VideoPreviewer(item);
            }
            else if (AudioPreviewer.IsItemSupported(item))
            {
                return new AudioPreviewer(item);
            }
            else if (WebBrowserPreviewer.IsItemSupported(item))
            {
                return CreateWebBrowserPreviewer(item);
            }
            else if (SqliteNS.SqlitePreviewer.IsItemSupported(item))
            {
                return new SqliteNS.SqlitePreviewer(item);
            }
            else if (ArchivePreviewer.IsItemSupported(item))
            {
                return new ArchivePreviewer(item);
            }
            else if (ShellPreviewHandlerPreviewer.IsItemSupported(item))
            {
                return new ShellPreviewHandlerPreviewer(item);
            }
            else if (DrivePreviewer.IsItemSupported(item))
            {
                return new DrivePreviewer(item);
            }
            else if (SpecialFolderPreviewer.IsItemSupported(item))
            {
                return new SpecialFolderPreviewer(item);
            }
            else if (WebBrowserPreviewer.IsFallbackCandidate(item))
            {
                // No recognized extension. The content check is done asynchronously in
                // LoadDisplayInfoAsync; if it isn't text, the previewer fails over to
                // the default/info preview.
                return CreateWebBrowserPreviewer(item);
            }

            // Other previewer types check their supported file types here
            return CreateDefaultPreviewer(item);
        }

        public IPreviewer CreateDefaultPreviewer(IFileSystemItem file)
        {
            PowerToysTelemetry.Log.WriteEvent(new ErrorEvent() { Failure = ErrorEvent.FailureType.FileNotSupported });
            return new UnsupportedFilePreviewer(file);
        }

        private WebBrowserPreviewer CreateWebBrowserPreviewer(IFileSystemItem item)
        {
            return _useCurrentDispatcher
                ? new WebBrowserPreviewer(item, _previewSettings)
                : new WebBrowserPreviewer(item, _previewSettings, dispatcher: null);
        }
    }
}
