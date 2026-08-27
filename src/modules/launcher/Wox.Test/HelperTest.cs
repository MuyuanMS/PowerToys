// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
//
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wox.Infrastructure;

namespace Wox.Test
{
    [TestClass]
    public class HelperTest
    {
        [TestMethod]
        public void ReplaceCommandArgumentKeepsAlreadyQuotedPlaceholderUnchanged()
        {
            var result = Helper.ReplaceCommandArgument("--single-argument \"%1\"", "multiple words");

            Assert.AreEqual("--single-argument \"multiple words\"", result);
        }

        [TestMethod]
        public void ReplaceCommandArgumentQuotesUnquotedPlaceholder()
        {
            var result = Helper.ReplaceCommandArgument("--single-argument %1", "multiple words");

            Assert.AreEqual("--single-argument \"multiple words\"", result);
        }

        [TestMethod]
        public void ReplaceCommandArgumentEscapesEmbeddedQuotesAndBackslashes()
        {
            var result = Helper.ReplaceCommandArgument("--single-argument %1", "C:\\Program Files\\\"search\"");

            Assert.AreEqual("--single-argument \"C:\\Program Files\\\\\\\"search\\\"\"", result);
        }

        [TestMethod]
        public void ReplaceCommandArgumentEscapesTrailingBackslash()
        {
            var result = Helper.ReplaceCommandArgument("--single-argument %1", "C:\\Program Files\\");

            Assert.AreEqual("--single-argument \"C:\\Program Files\\\\\"", result);
        }

        [TestMethod]
        public void ReplaceCommandArgumentUsesEmptyArgumentForNull()
        {
            var result = Helper.ReplaceCommandArgument("--single-argument %1", null);

            Assert.AreEqual("--single-argument \"\"", result);
        }
    }
}
