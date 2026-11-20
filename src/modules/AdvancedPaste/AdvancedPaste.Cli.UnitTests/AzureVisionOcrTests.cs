// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdvancedPaste.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedPaste.Cli.UnitTests;

[TestClass]
public class AzureVisionOcrTests
{
    private const string OperationLocation = "https://demo.cognitiveservices.azure.com/vision/v3.2/read/analyzeResults/d3d3d3d3-eeee-ffff-aaaa-b4b4b4b4b4b4";

    [DataTestMethod]
    [DataRow("http://demo.cognitiveservices.azure.com/")]
    [DataRow("https://demo.cognitiveservices.azure.com.evil.example/")]
    [DataRow("https://secret@demo.cognitiveservices.azure.com/")]
    [DataRow("https://demo.cognitiveservices.azure.com/vision/v3.2/")]
    [DataRow("https://demo.cognitiveservices.azure.com/?key=secret")]
    public void Endpoint_RejectsInsecureOrUnexpectedDestinations(string endpoint)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AzureVisionOcr.ValidateEndpoint(endpoint));
    }

    [TestMethod]
    public async Task Read_SendsBytesAndHeaderToReadApi_AndPreservesLineOrder()
    {
        const string key = "unit-test-key";
        var image = new byte[] { 137, 80, 78, 71 };
        using var handler = new StubHandler(async request =>
        {
            Assert.IsFalse(request.RequestUri!.AbsoluteUri.Contains(key, StringComparison.Ordinal));
            Assert.AreEqual(key, string.Join(string.Empty, request.Headers.GetValues("Ocp-Apim-Subscription-Key")));
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("https://demo.cognitiveservices.azure.com/vision/v3.2/read/analyze", request.RequestUri.AbsoluteUri);
                Assert.AreEqual("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
                CollectionAssert.AreEqual(image, await request.Content.ReadAsByteArrayAsync());
                return AcceptedOperation(OperationLocation);
            }

            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(OperationLocation, request.RequestUri.AbsoluteUri);
            Assert.IsNull(request.Content);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"status":"succeeded","analyzeResult":{"readResults":[{"lines":[{"text":"First line"},{"text":"Second line"}]},{"lines":[{"text":"\u4F60\u597D"}]}]}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var client = new HttpClient(handler);

        var text = await AzureVisionOcr.ReadAsync(new Uri("https://demo.cognitiveservices.azure.com/"), key, image, client, CancellationToken.None);

        Assert.AreEqual("First line" + Environment.NewLine + "Second line" + Environment.NewLine + "\u4F60\u597D", text);
        Assert.AreEqual(2, handler.Calls);
    }

    [DataTestMethod]
    [DataRow("https://evil.example/vision/v3.2/read/analyzeResults/d3d3d3d3-eeee-ffff-aaaa-b4b4b4b4b4b4")]
    [DataRow("http://demo.cognitiveservices.azure.com/vision/v3.2/read/analyzeResults/d3d3d3d3-eeee-ffff-aaaa-b4b4b4b4b4b4")]
    [DataRow("https://demo.cognitiveservices.azure.com/unexpected")]
    [DataRow("https://demo.cognitiveservices.azure.com/vision/v3.2/read/analyzeResults/d3d3d3d3-eeee-ffff-aaaa-b4b4b4b4b4b4?key=secret")]
    public async Task Read_RejectsUnexpectedOperationUrlBeforeForwardingKey(string location)
    {
        using var handler = new StubHandler(_ => Task.FromResult(AcceptedOperation(location)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsExactlyAsync<CliActionUnavailableException>(() =>
            AzureVisionOcr.ReadAsync(new Uri("https://demo.cognitiveservices.azure.com/"), "unit-test-key", [1], client, CancellationToken.None));

        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task Read_PollsRunningOperationWithoutResubmittingImage()
    {
        var polls = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual(0, polls);
                return Task.FromResult(AcceptedOperation(OperationLocation));
            }

            polls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(polls == 1 ? """{"status":"running"}""" :
                    """{"status":"succeeded","analyzeResult":{"readResults":[{"lines":[{"text":"Complete"}]}]}}"""),
            });
        });
        using var client = new HttpClient(handler);

        var text = await AzureVisionOcr.ReadAsync(new Uri("https://demo.cognitiveservices.azure.com/"), "unit-test-key", [1], client, CancellationToken.None);

        Assert.AreEqual("Complete", text);
        Assert.AreEqual(2, polls);
        Assert.AreEqual(3, handler.Calls);
    }

    [TestMethod]
    public async Task Read_CancellationAfterUploadDoesNotPollOrResubmit()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(AcceptedOperation(OperationLocation));
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AzureVisionOcr.ReadAsync(new Uri("https://demo.cognitiveservices.azure.com/"), "unit-test-key", [1], client, cancellation.Token));

        Assert.AreEqual(1, handler.Calls);
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Redirect)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public async Task Read_RejectsHttpFailuresWithoutRetryFallbackOrSecretDisclosure(HttpStatusCode status)
    {
        using var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("untrusted response body containing unit-test-key"),
        }));
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsExactlyAsync<CliActionUnavailableException>(() =>
            AzureVisionOcr.ReadAsync(new Uri("https://demo.cognitiveservices.azure.com/"), "unit-test-key", [1], client, CancellationToken.None));

        StringAssert.Contains(error.Message, ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsFalse(error.Message.Contains("unit-test-key", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public void ParseText_RejectsEmptyOrMissingResults()
    {
        using var empty = JsonDocument.Parse("""{"analyzeResult":{"readResults":[]}}""");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsExactly<CliActionUnavailableException>(() => AzureVisionOcr.ParseText(empty.RootElement));
        Assert.ThrowsExactly<CliActionUnavailableException>(() => AzureVisionOcr.ParseText(missing.RootElement));
    }

    private static HttpResponseMessage AcceptedOperation(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        response.Headers.Add("Operation-Location", location);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request);
        }
    }
}
