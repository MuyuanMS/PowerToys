// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.PerformanceMonitor.UnitTests;

[TestClass]
public class NetworkConnectionDetailsTests
{
    private static readonly IPAddress[] Addresses =
    [
        IPAddress.Parse("fe80::1c2b:3a4d:5e6f:7081"),
        IPAddress.Parse("169.254.10.20"),
        IPAddress.Parse("fd8a:b83e::1"),
        IPAddress.Parse("192.168.1.20"),
        IPAddress.Parse("2001:db8::42"),
        IPAddress.Loopback,
    ];

    [TestMethod]
    public void NonLinkLocalAddressesComeBeforeLinkLocalOnes()
    {
        var result = NetworkConnectionDetails.PickAddresses(Addresses);

        Assert.AreEqual("192.168.1.20", result.IPv4);
        Assert.AreEqual("2001:db8::42", result.IPv6);
        Assert.AreEqual("fd8a:b83e::1", NetworkConnectionDetails.PickAddresses([Addresses[0], Addresses[2]]).IPv6);
    }

    [TestMethod]
    public void LinkLocalAddressIsShownWhenItIsTheOnlyOne()
    {
        var result = NetworkConnectionDetails.PickAddresses([Addresses[1], IPAddress.Loopback]);

        Assert.AreEqual("169.254.10.20", result.IPv4);
        Assert.AreEqual(string.Empty, result.IPv6);
    }

    [TestMethod]
    public void CommonAdapterTypesHaveNames()
    {
        Assert.AreEqual("NetworkUsage_Widget_Template/Type_WiFi", NetworkConnectionDetails.GetTypeResourceKey(NetworkInterfaceType.Wireless80211));
        Assert.AreEqual("NetworkUsage_Widget_Template/Type_Ethernet", NetworkConnectionDetails.GetTypeResourceKey(NetworkInterfaceType.GigabitEthernet));
        Assert.AreEqual("NetworkUsage_Widget_Template/Type_Cellular", NetworkConnectionDetails.GetTypeResourceKey(NetworkInterfaceType.Wwanpp2));
        Assert.IsNull(NetworkConnectionDetails.GetTypeResourceKey(NetworkInterfaceType.Tunnel));
    }

    [TestMethod]
    public void AdapterTypesHaveIcons()
    {
        Assert.AreEqual("Wifi1", NetworkConnectionDetails.GetTypeIconName(NetworkInterfaceType.Wireless80211));
        Assert.AreEqual("NetworkAdapter", NetworkConnectionDetails.GetTypeIconName(NetworkInterfaceType.Ethernet));
        Assert.AreEqual("CellularData1", NetworkConnectionDetails.GetTypeIconName(NetworkInterfaceType.Wwanpp));
        Assert.AreEqual("Globe", NetworkConnectionDetails.GetTypeIconName(NetworkInterfaceType.Tunnel));
        Assert.AreEqual("Globe", NetworkConnectionDetails.GetTypeIconName(null));
    }

    [TestMethod]
    public void InterfaceIdIsReadFromTheAdapterId()
    {
        var id = Guid.NewGuid();

        Assert.AreEqual(id, SystemNetworkUsageWidgetPage.TryGetInterfaceId("network-interface:" + id.ToString("D")));
        Assert.IsNull(SystemNetworkUsageWidgetPage.TryGetInterfaceId("network-interface:00000000000ABCDE"));
        Assert.IsNull(SystemNetworkUsageWidgetPage.TryGetInterfaceId("all-physical-network-adapters"));
    }
}
