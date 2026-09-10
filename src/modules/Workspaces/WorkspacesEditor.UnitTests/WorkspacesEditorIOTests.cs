// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkspacesEditor.Utils;

namespace WorkspacesEditor.UnitTests
{
    [TestClass]
    public class WorkspacesEditorIOTests
    {
        [TestMethod]
        [TestCategory("Utils.WorkspacesEditorIO")]
        public void GetIncludedApplications_ExcludedApplication_OmitsIt()
        {
            var project = TestHelpers.CreateProject("Test", 0, 0, "Included", "Excluded");
            project.Applications[1].IsIncluded = false;

            var applications = WorkspacesEditorIO.GetIncludedApplications(project).ToList();

            Assert.HasCount(1, applications);
            Assert.AreEqual("Included", applications[0].AppName);
        }
    }
}
