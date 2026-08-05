// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml.Data;
using WorkspacesEditor.Helpers;

namespace WorkspacesEditor.Converters
{
    public sealed partial class LaunchButtonNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string launch = ResourceLoaderInstance.ResourceLoader?.GetString("Launch") ?? "Launch";
            return $"{launch} {value}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
