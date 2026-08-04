// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.UI.Xaml.Data;
using WorkspacesEditor.Helpers;

namespace WorkspacesEditor.Converters
{
    public sealed class LaunchButtonNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string launch = ResourceLoaderInstance.ResourceLoader?.GetString("Launch") ?? "Launch";
            return $"{launch} {value}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class MoreOptionsButtonNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string moreOptions = ResourceLoaderInstance.ResourceLoader?.GetString("MoreOptions") ?? "More options for";
            return $"{moreOptions} {value}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
