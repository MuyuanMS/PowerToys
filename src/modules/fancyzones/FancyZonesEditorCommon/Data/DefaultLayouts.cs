// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using static FancyZonesEditorCommon.Data.DefaultLayouts;

namespace FancyZonesEditorCommon.Data
{
    public class DefaultLayouts : EditorData<DefaultLayoutsListWrapper>
    {
        public string File
        {
            get
            {
                return FancyZonesPaths.DefaultLayouts;
            }
        }

        public struct DefaultLayoutWrapper
        {
            public struct LayoutWrapper
            {
                public LayoutWrapper()
                {
                }

                public string Uuid { get; set; }

                public string Type { get; set; }

                public bool ShowSpacing { get; set; } = LayoutDefaultSettings.DefaultShowSpacing;

                public int Spacing { get; set; } = LayoutDefaultSettings.DefaultSpacing;

                public int ZoneCount { get; set; } = LayoutDefaultSettings.DefaultZoneCount;

                public int SensitivityRadius { get; set; } = LayoutDefaultSettings.DefaultSensitivityRadius;
            }

            public string MonitorConfiguration { get; set; }

            public LayoutWrapper Layout { get; set; }
        }

        public struct DefaultLayoutsListWrapper
        {
            public List<DefaultLayoutWrapper> DefaultLayouts { get; set; }
        }
    }
}
