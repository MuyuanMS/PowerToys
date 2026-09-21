// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

namespace Microsoft.CmdPal.UI.ViewModels;

internal interface IActivationSettlementPage
{
    event EventHandler? SearchSettlementChanged;

    bool CurrentFetchIsSettledFor(string query);
}
