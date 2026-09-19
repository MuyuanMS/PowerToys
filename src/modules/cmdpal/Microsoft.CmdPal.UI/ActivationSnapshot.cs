// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Microsoft.CmdPal.UI;

internal sealed record ActivationSnapshot(ExtendedActivationKind Kind, Uri? ProtocolUri);
