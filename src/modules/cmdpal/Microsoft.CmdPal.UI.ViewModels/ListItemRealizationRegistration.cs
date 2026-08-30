// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.UI.ViewModels;

public readonly struct ListItemRealizationRegistration
{
    private readonly RegistrationState? _state;

    internal ListItemRealizationRegistration(ListItemInitializationDemand? demand)
    {
        _state = demand is null ? null : new RegistrationState(demand);
    }

    public bool IsValid => _state?.IsValid == true;

    public bool IsFor(ListItemViewModel item) => _state?.IsFor(item) == true;

    public void Release() => _state?.Release();

    private sealed class RegistrationState(ListItemInitializationDemand demand)
    {
        private ListItemInitializationDemand? _demand = demand;

        internal bool IsValid => Volatile.Read(ref _demand)?.IsActive == true;

        internal bool IsFor(ListItemViewModel item)
        {
            var demand = Volatile.Read(ref _demand);
            return demand?.IsActive == true && ReferenceEquals(demand.Item, item);
        }

        internal void Release()
        {
            Interlocked.Exchange(ref _demand, null)?.Release();
        }
    }
}
