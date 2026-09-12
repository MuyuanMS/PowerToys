// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace Microsoft.CmdPal.Ext.TimeDate.Pages;

/// <summary>
/// A list page that starts its live work only while CmdPal is displaying it.
/// CmdPal attaches an <see cref="ItemsChanged"/> handler when the page is loaded
/// and removes it after the page is no longer in use.
/// </summary>
internal abstract partial class OnLoadDynamicListPage : Page, IDynamicListPage
{
    private readonly Lock _loadLock = new();
    private string _searchText = string.Empty;

#pragma warning disable CS0067 // Invoked through RaiseItemsChanged.
    private event TypedEventHandler<object, IItemsChangedEventArgs>? InternalItemsChanged;
#pragma warning restore CS0067

    public virtual string PlaceholderText { get; set => SetProperty(ref field, value); } = string.Empty;

    public virtual string SearchText
    {
        get => _searchText;
        set
        {
            var oldSearch = _searchText;
            SetSearchNoUpdate(value);
            UpdateSearchText(oldSearch, value);
        }
    }

    public virtual bool ShowDetails { get; set => SetProperty(ref field, value); }

    public virtual bool HasMoreItems { get; set => SetProperty(ref field, value); }

    public virtual IFilters? Filters { get; set => SetProperty(ref field, value); }

    public virtual IGridProperties? GridProperties { get; set => SetProperty(ref field, value); }

    public virtual ICommandItem? EmptyContent { get; set => SetProperty(ref field, value); }

    public event TypedEventHandler<object, IItemsChangedEventArgs> ItemsChanged
    {
        add
        {
            lock (_loadLock)
            {
                var wasEmpty = InternalItemsChanged is null;
                InternalItemsChanged += value;
                if (wasEmpty)
                {
                    Loaded();
                }
            }
        }

        remove
        {
            lock (_loadLock)
            {
                var hadSubscribers = InternalItemsChanged is not null;
                InternalItemsChanged -= value;
                if (hadSubscribers && InternalItemsChanged is null)
                {
                    Unloaded();
                }
            }
        }
    }

    public void LoadMore()
    {
    }

    public abstract IListItem[] GetItems();

    public abstract void UpdateSearchText(string oldSearch, string newSearch);

    protected void SetSearchNoUpdate(string newSearchText) => _searchText = newSearchText;

    protected void RaiseItemsChanged(int totalItems = -1)
    {
        TypedEventHandler<object, IItemsChangedEventArgs>? handlers;
        lock (_loadLock)
        {
            handlers = InternalItemsChanged;
        }

        EventHelpers.Raise(handlers, this, new ItemsChangedEventArgs(totalItems), handler => ItemsChanged -= handler);
    }

    protected abstract void Loaded();

    protected abstract void Unloaded();
}
