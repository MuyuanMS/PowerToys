// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AdvancedPaste.Helpers;
using AdvancedPaste.Models;
using AdvancedPaste.UnitTests.Mocks;
using AdvancedPaste.UnitTests.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AdvancedPaste.UnitTests.HelpersTests;

[TestClass]
public sealed class TransformHelpersTests
{
    [TestMethod]
    public void PasteFormats_PreservePersistedValues()
    {
        Assert.AreEqual(7, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.PasteAsHtmlFile)));
        Assert.AreEqual(8, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.TranscodeToMp3)));
        Assert.AreEqual(9, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.TranscodeToMp4)));
        Assert.AreEqual(10, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.KernelQuery)));
        Assert.AreEqual(11, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.CustomTextTransformation)));
        Assert.AreEqual(12, (int)Enum.Parse<PasteFormats>(nameof(PasteFormats.PasteAsJpgFile)));
    }

    [TestMethod]
    public async Task TransformToJpgFileProducesJpegFileAndRespectsQuality()
    {
        var lowQualitySize = await GetJpgOutputFileSizeAsync(10);
        var highQualitySize = await GetJpgOutputFileSizeAsync(100);

        Assert.IsTrue(
            lowQualitySize < highQualitySize,
            $"Expected low quality output ({lowQualitySize} bytes) to be smaller than high quality output ({highQualitySize} bytes)");
    }

    [TestMethod]
    public async Task TransformToJpgFileAcceptsTransparentPng()
    {
        var inputPackage = await CreateTransparentImageDataPackageAsync();

        var outputPackage = await TransformHelpers.TransformAsync(Enum.Parse<PasteFormats>(nameof(PasteFormats.PasteAsJpgFile)), inputPackage.GetView(), CancellationToken.None, new NoOpProgress());
        var outputFile = (await outputPackage.GetView().GetStorageItemsAsync()).Single() as StorageFile;
        Assert.IsNotNull(outputFile);

        using var readStream = await outputFile.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(readStream);
        Assert.AreEqual(BitmapDecoder.JpegDecoderId, decoder.DecoderInformation.CodecId);
        Assert.AreEqual(1u, decoder.PixelWidth);
        Assert.AreEqual(1u, decoder.PixelHeight);

        await outputPackage.GetView().TryCleanupAfterDelayAsync(TimeSpan.Zero);
    }

    [TestMethod]
    public async Task TransformToJpgFileAcceptsGrayscalePng()
    {
        var inputPackage = await ResourceUtils.GetImageAssetAsDataPackageAsync("grayscale.png");
        using var stream = await (await inputPackage.GetView().GetBitmapAsync()).OpenReadAsync();
        var inputDecoder = await BitmapDecoder.CreateAsync(stream);
        using var inputBitmap = await inputDecoder.GetSoftwareBitmapAsync();
        Assert.AreEqual(BitmapPixelFormat.Bgra8, inputBitmap.BitmapPixelFormat);

        var outputPackage = await TransformHelpers.TransformAsync(Enum.Parse<PasteFormats>(nameof(PasteFormats.PasteAsJpgFile)), inputPackage.GetView(), CancellationToken.None, new NoOpProgress());
        var outputFile = (await outputPackage.GetView().GetStorageItemsAsync()).Single() as StorageFile;
        Assert.IsNotNull(outputFile);

        using (var readStream = await outputFile.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(readStream);
            Assert.AreEqual(BitmapDecoder.JpegDecoderId, decoder.DecoderInformation.CodecId);
        }

        await outputPackage.GetView().TryCleanupAfterDelayAsync(TimeSpan.Zero);
    }

    private static async Task<ulong> GetJpgOutputFileSizeAsync(int jpgQuality)
    {
        var inputPackage = await ResourceUtils.GetImageAssetAsDataPackageAsync("image_with_text_example.png");

        var outputPackage = await TransformHelpers.TransformAsync(Enum.Parse<PasteFormats>(nameof(PasteFormats.PasteAsJpgFile)), inputPackage.GetView(), CancellationToken.None, new NoOpProgress(), jpgQuality);

        var outputItems = await outputPackage.GetView().GetStorageItemsAsync();
        Assert.AreEqual(1, outputItems.Count);
        var outputFile = outputItems.Single() as StorageFile;
        Assert.IsNotNull(outputFile);
        Assert.AreEqual(".jpg", outputFile.FileType, ignoreCase: true, CultureInfo.InvariantCulture);

        using (var readStream = await outputFile.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(readStream);
            Assert.AreEqual(BitmapDecoder.JpegDecoderId, decoder.DecoderInformation.CodecId);
        }

        var outputFileSize = (await outputFile.GetBasicPropertiesAsync()).Size;
        await outputPackage.GetView().TryCleanupAfterDelayAsync(TimeSpan.Zero);
        return outputFileSize;
    }

    private static async Task<DataPackage> CreateTransparentImageDataPackageAsync()
    {
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 1, 1, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray([0, 0, 0, 0]));

        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        stream.Seek(0);

        DataPackage package = new();
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        return package;
    }
}
