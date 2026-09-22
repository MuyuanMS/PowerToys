// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenTranslator.UnitTests;

[TestClass]
public class ScreenTranslatorLanguageCatalogTests
{
    private static readonly string[] ExpectedAzureLanguages = ["ar", "hi"];

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [TestMethod]
    public async Task AzureCatalog_UsesServiceMetadata()
    {
        const string responseBody = """
            {
              "translation": {
                "ar": { "name": "Arabic", "nativeName": "العربية", "dir": "rtl" },
                "hi": { "name": "Hindi", "nativeName": "हिन्दी", "dir": "ltr" }
              }
            }
            """;
        using HttpClient httpClient = new(new FakeHttpMessageHandler(request =>
        {
            Assert.IsTrue(request.RequestUri!.AbsoluteUri.Contains("/languages?api-version=3.0", StringComparison.Ordinal));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }));

        var languages = await ScreenTranslatorLanguageCatalog.GetTranslationLanguagesAsync(
            "AzureTranslator",
            "https://api.cognitive.microsofttranslator.com",
            null,
            httpClient);

        CollectionAssert.AreEquivalent(
            ExpectedAzureLanguages,
            languages.Select(language => language.Code).ToArray());
    }

    [TestMethod]
    public void SourceCatalog_DisablesLanguagesMissingFromLocalOcr()
    {
        ScreenTranslatorLanguageOption[] translationLanguages =
        [
            new("en", "English"),
            new("ja", "Japanese"),
            new("ar", "Arabic"),
        ];

        var sourceLanguages = ScreenTranslatorLanguageCatalog.CreateSourceOptions(
            translationLanguages,
            cloudOcrEnabled: false,
            ["en-US", "ja"]);

        Assert.IsTrue(sourceLanguages.Single(language => language.Code == "auto").IsEnabled);
        Assert.IsTrue(sourceLanguages.Single(language => language.Code == "en").IsEnabled);
        Assert.IsTrue(sourceLanguages.Single(language => language.Code == "ja").IsEnabled);
        Assert.IsFalse(sourceLanguages.Single(language => language.Code == "ar").IsEnabled);
    }

    [TestMethod]
    public void SourceCatalog_EnablesAllLanguagesForCloudOcr()
    {
        ScreenTranslatorLanguageOption[] translationLanguages =
        [
            new("en", "English"),
            new("ar", "Arabic"),
        ];

        var sourceLanguages = ScreenTranslatorLanguageCatalog.CreateSourceOptions(
            translationLanguages,
            cloudOcrEnabled: true,
            Array.Empty<string>());

        Assert.IsTrue(sourceLanguages.All(language => language.IsEnabled));
    }

    [TestMethod]
    public void LanguageEquivalence_PreservesChineseScriptVariants()
    {
        Assert.IsTrue(ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags("zh-Hans", "zh-CN"));
        Assert.IsTrue(ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags("zh-Hant", "zh-TW"));
        Assert.IsFalse(ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags("zh-Hans", "zh-Hant"));
    }
}
