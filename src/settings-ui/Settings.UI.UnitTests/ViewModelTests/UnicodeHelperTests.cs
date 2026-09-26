// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerAccent.Common;

namespace ViewModelTests
{
    [TestClass]
    public sealed class UnicodeHelperTests
    {
        [TestMethod]
        public void GetCharacterName_BmpCharacter_ReturnsUnicodeName()
        {
            Assert.AreEqual("LATIN CAPITAL LETTER A", UnicodeHelper.GetCharacterName("A"));
        }

        [TestMethod]
        public void GetCharacterName_SupplementaryCharacter_ReturnsUnicodeName()
        {
            Assert.AreEqual("GRINNING FACE", UnicodeHelper.GetCharacterName("\U0001F600"));
        }

        [TestMethod]
        public void GetCharacterName_MultiCodePointString_JoinsUnicodeNames()
        {
            Assert.AreEqual("DEGREE SIGN + LATIN CAPITAL LETTER C", UnicodeHelper.GetCharacterName("°C"));
        }

        [TestMethod]
        public void GetCharacterName_UnnamedCharacter_ReturnsNull()
        {
            Assert.IsNull(UnicodeHelper.GetCharacterName("\uE000"));
        }
    }
}
