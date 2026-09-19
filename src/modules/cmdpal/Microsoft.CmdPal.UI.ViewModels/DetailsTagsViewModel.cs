// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;

namespace Microsoft.CmdPal.UI.ViewModels;

public partial class DetailsTagsViewModel(
    IDetailsElement _detailsElement,
    WeakReference<IPageContext> context) : DetailsElementViewModel(_detailsElement, context)
{
    public List<TagViewModel> Tags { get; private set; } = [];

    public bool HasTags => Tags.Count > 0;

    private readonly ExtensionObject<IDetailsTags> _dataModel =
        new(_detailsElement.Data as IDetailsTags);

    public override void InitializeProperties()
    {
        base.InitializeProperties();
        var model = _dataModel.Unsafe;
        if (model is null)
        {
            return;
        }

        var tags = new List<TagViewModel>();
        try
        {
            foreach (var tag in model.Tags ?? [])
            {
                var vm = new TagViewModel(tag, PageContext);
                vm.InitializeProperties();
                tags.Add(vm);
            }
        }
        catch
        {
            tags.ForEach(vm => vm.SafeCleanup());
            throw;
        }

        Tags = tags;
        UpdateProperty(nameof(HasTags));
        UpdateProperty(nameof(Tags));
    }

    protected override void UnsafeCleanup()
    {
        var previous = Tags;
        Tags = [];
        previous.ForEach(vm => vm.SafeCleanup());
        base.UnsafeCleanup();
    }
}
