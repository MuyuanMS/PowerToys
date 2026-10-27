// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CmdPal.UI.ViewModels.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed class InProcObjectTests
{
    [TestMethod]
    public void Safe_PreservesOriginalObject()
    {
        var item = new ListItem { Title = "Original" };
        var wrapped = new InProcObject<IListItem>(item);

        Assert.AreSame(item, wrapped.Safe);
        item.Title = "Updated";
        Assert.AreEqual("Updated", wrapped.Safe.Title);
    }

    [TestMethod]
    public void Equality_UsesWrappedValueAndMatchingWrapperType()
    {
        var item = new ListItem();
        var wrapped = new InProcObject<IListItem>(item);
        var same = new InProcObject<IListItem>(item);

        Assert.IsTrue(wrapped.Equals(same));
        Assert.AreEqual(wrapped.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(wrapped.Equals(new InProcObject<IListItem>(new ListItem())));
        Assert.IsFalse(wrapped.Equals(new ExtensionObject<IListItem>(item)));
        Assert.IsFalse(wrapped.Equals(item));
        Assert.IsFalse(wrapped.Equals(null));
    }

    [TestMethod]
    public void NullableValue_MatchesExtensionObjectSemantics()
    {
        var wrapped = new InProcObject<IListItem?>(null);
        var other = new InProcObject<IListItem?>(null);

        Assert.IsNull(wrapped.Safe);
        Assert.IsFalse(wrapped.HasValue);
        Assert.IsTrue(wrapped.Equals(wrapped));
        Assert.IsTrue(wrapped.Equals(other));
        Assert.AreEqual(wrapped.GetHashCode(), other.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_DoesNotCallWrappedObject()
    {
        var target = new ThrowingHashCode();
        var wrapped = new InProcObject<object>(target);
        var same = new InProcObject<object>(target);

        Assert.IsTrue(wrapped.Equals(same));
        Assert.AreEqual(wrapped.GetHashCode(), same.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_RemainsStableWhenWrappedObjectChanges()
    {
        var target = new MutableHashCode { Value = 1 };
        var wrapped = new InProcObject<object>(target);
        var same = new InProcObject<object>(target);
        var set = new HashSet<InProcObject<object>> { wrapped };
        var originalHash = wrapped.GetHashCode();

        target.Value = 2;

        Assert.AreEqual(originalHash, wrapped.GetHashCode());
        Assert.IsTrue(set.Contains(wrapped));
        Assert.IsTrue(set.Contains(same));
    }

    private sealed class ThrowingHashCode
    {
        public override int GetHashCode() => throw new InvalidOperationException("Wrapped object hashing must not be called.");
    }

    private sealed class MutableHashCode
    {
        public int Value { get; set; }

        public override int GetHashCode() => Value;
    }
}
