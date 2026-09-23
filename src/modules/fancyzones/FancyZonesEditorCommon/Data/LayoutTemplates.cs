// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using static FancyZonesEditorCommon.Data.LayoutTemplates;

namespace FancyZonesEditorCommon.Data
{
    public class LayoutTemplates : EditorData<TemplateLayoutsListWrapper>
    {
        public string File
        {
            get
            {
                return FancyZonesPaths.LayoutTemplates;
            }
        }

        public struct TemplateLayoutWrapper
        {
            public TemplateLayoutWrapper()
            {
            }

            public string Type { get; set; }

            public bool ShowSpacing { get; set; } = LayoutDefaultSettings.DefaultShowSpacing;

            public int Spacing { get; set; } = LayoutDefaultSettings.DefaultSpacing;

            public int ZoneCount { get; set; } = LayoutDefaultSettings.DefaultZoneCount;

            public int SensitivityRadius { get; set; } = LayoutDefaultSettings.DefaultSensitivityRadius;
        }

        public struct TemplateLayoutsListWrapper
        {
            public List<TemplateLayoutWrapper> LayoutTemplates { get; set; }
        }
    }
}
