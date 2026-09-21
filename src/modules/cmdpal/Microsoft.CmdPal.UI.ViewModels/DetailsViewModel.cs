// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Microsoft.CmdPal.UI.ViewModels;

public partial class DetailsViewModel : ExtensionObjectViewModel
{
    private readonly Lock _lifecycleLock = new();
    private readonly ExtensionObject<IDetails> _detailsModel;
    private INotifyPropChanged? _observableDetails;
    private bool _isSubscribed;
    private bool _isCleanedUp;

    // Remember - "observable" properties from the model (via PropChanged)
    // cannot be marked [ObservableProperty]
    public IconInfoViewModel HeroImage { get; private set; } = new(null);

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public ContentSize? Size { get; private set; } = ContentSize.Small;

    // Metadata is an array of IDetailsElement,
    //   where IDetailsElement = {IDetailsTags, IDetailsLink, IDetailsSeparator}
    public List<DetailsElementViewModel> Metadata { get; private set; } = [];

    public ObservableCollection<ContentViewModel> Content { get; } = [];

    public DetailsViewModel(IDetails details, WeakReference<IPageContext> context)
        : base(context)
    {
        _detailsModel = new(details);
    }

    private void Model_PropChanged(object sender, IPropChangedEventArgs args)
    {
        lock (_lifecycleLock)
        {
            if (_isCleanedUp)
            {
                return;
            }
        }

        try
        {
            FetchProperty(args.PropertyName);
        }
        catch (Exception ex)
        {
            ShowException(ex);
        }
    }

    private void FetchProperty(string propertyName)
    {
        var model = _detailsModel.Unsafe;
        if (model is null)
        {
            return;
        }

        switch (propertyName)
        {
            case nameof(IDetails.Title):
                var title = model.Title ?? string.Empty;
                lock (_lifecycleLock)
                {
                    if (_isCleanedUp)
                    {
                        return;
                    }

                    Title = title;
                }

                UpdateProperty(nameof(Title));
                break;
            case nameof(IDetails.Body):
                var body = model.Body ?? string.Empty;
                lock (_lifecycleLock)
                {
                    if (_isCleanedUp)
                    {
                        return;
                    }

                    Body = body;
                }

                UpdateProperty(nameof(Body));
                break;
            case nameof(IDetails.HeroImage):
                var heroImage = new IconInfoViewModel(model.HeroImage);
                heroImage.InitializeProperties();
                lock (_lifecycleLock)
                {
                    if (_isCleanedUp)
                    {
                        return;
                    }

                    HeroImage = heroImage;
                }

                UpdateProperty(nameof(HeroImage));
                break;
            case nameof(IDetails.Metadata):
                var metadata = BuildMetadata(model);
                List<DetailsElementViewModel>? replacedMetadata = null;
                lock (_lifecycleLock)
                {
                    if (!_isCleanedUp)
                    {
                        replacedMetadata = Metadata;
                        Metadata = metadata;
                    }
                }

                if (replacedMetadata is null)
                {
                    metadata.ForEach(item => item.SafeCleanup());
                    return;
                }

                replacedMetadata.ForEach(item => item.SafeCleanup());
                UpdateProperty(nameof(Metadata));
                break;

            // here be dragons: IDetails2 exposes a method GetContent() to build
            // the content object. But the property change comes in under the name
            // "Content". So yes, this intentionally uses the toolkit's property name
            case nameof(Details.Content):
                RebuildContent(model);
                break;
        }
    }

    private List<DetailsElementViewModel> BuildMetadata(IDetails model)
    {
        var newMetadata = new List<DetailsElementViewModel>();
        var meta = model.Metadata;
        if (meta is not null)
        {
            foreach (var element in meta)
            {
                DetailsElementViewModel? vm = element.Data switch
                {
                    IDetailsSeparator => new DetailsSeparatorViewModel(element, this.PageContext),
                    IDetailsLink => new DetailsLinkViewModel(element, this.PageContext),
                    IDetailsCommands => new DetailsCommandsViewModel(element, this.PageContext),
                    IDetailsTags => new DetailsTagsViewModel(element, this.PageContext),
                    _ => null,
                };
                if (vm is not null)
                {
                    vm.InitializeProperties();
                    newMetadata.Add(vm);
                }
            }
        }

        return newMetadata;
    }

    public override void InitializeProperties()
    {
        var model = _detailsModel.Unsafe;
        if (model is null)
        {
            return;
        }

        lock (_lifecycleLock)
        {
            if (_isCleanedUp)
            {
                return;
            }

            // Subscribe to PropChanged if the model supports it (only subscribe once).
            if (!_isSubscribed && model is INotifyPropChanged observable)
            {
                observable.PropChanged += Model_PropChanged;
                if (_isCleanedUp)
                {
                    observable.PropChanged -= Model_PropChanged;
                    return;
                }

                _observableDetails = observable;
                _isSubscribed = true;
            }
        }

        var title = model.Title ?? string.Empty;
        var body = model.Body ?? string.Empty;
        var heroImage = new IconInfoViewModel(model.HeroImage);
        heroImage.InitializeProperties();

        ContentSize? size = ContentSize.Small;
        if (model is IExtendedAttributesProvider provider)
        {
            if (provider.GetProperties()?.TryGetValue("Size", out var rawValue) == true)
            {
                if (rawValue is int sizeAsInt)
                {
                    size = (ContentSize)sizeAsInt;
                }
            }
        }

        size ??= ContentSize.Small;
        var metadata = BuildMetadata(model);
        lock (_lifecycleLock)
        {
            if (_isCleanedUp)
            {
                metadata.ForEach(item => item.SafeCleanup());
                return;
            }

            Title = title;
            Body = body;
            HeroImage = heroImage;
            Size = size;
            Metadata = metadata;
        }

        UpdateProperty(nameof(Title));
        UpdateProperty(nameof(Body));
        UpdateProperty(nameof(HeroImage));
        UpdateProperty(nameof(Size));
        UpdateProperty(nameof(Metadata));
        RebuildContent(model);
    }

    private void RebuildContent(IDetails model)
    {
        List<ContentViewModel> content = [];
        if (model is IDetails2 details2)
        {
            foreach (var item in details2.GetContent())
            {
                var viewModel = CommandPaletteContentPageViewModel.CreateViewModel(item, PageContext);
                if (viewModel is not null)
                {
                    viewModel.InitializeProperties();
                    content.Add(viewModel);
                }
            }
        }

        // Now, back to a UI thread to update the observable collection
        DoOnUiThread(
            () =>
            {
                var published = false;
                lock (_lifecycleLock)
                {
                    if (!_isCleanedUp)
                    {
                        ListHelpers.InPlaceUpdateList(Content, content);
                        UpdateProperty(nameof(Content));
                        published = true;
                    }
                }

                if (!published)
                {
                    content.ForEach(item => item.SafeCleanup());
                }
            });
    }

    protected override void UnsafeCleanup()
    {
        base.UnsafeCleanup();

        List<DetailsElementViewModel> metadata;
        lock (_lifecycleLock)
        {
            _isCleanedUp = true;
            Title = string.Empty;
            Body = string.Empty;
            HeroImage = new(null);
            Size = ContentSize.Small;
            metadata = Metadata;
            Metadata = [];
        }

        metadata.ForEach(item => item.SafeCleanup());
        if (!TryDoOnUiThread(ClearContent))
        {
            ClearContent();
        }

        lock (_lifecycleLock)
        {
            if (_isSubscribed && _observableDetails is not null)
            {
                _observableDetails.PropChanged -= Model_PropChanged;
                _observableDetails = null;
                _isSubscribed = false;
            }
        }
    }

    private void ClearContent()
    {
        List<ContentViewModel> content;
        lock (_lifecycleLock)
        {
            content = [.. Content];
            Content.Clear();
        }

        content.ForEach(item => item.SafeCleanup());
    }
}
