// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

using Provider = Microsoft.PowerToys.ThumbnailHandler.ThreeMf.ThreeMfThumbnailProvider;

namespace ThreeMfThumbnailProvider.FuzzTests
{
    public class FuzzTests
    {
        private static readonly BlockingCollection<FuzzRequest> Requests = new();
        private static readonly Thread StaWorker = StartStaWorker();

        // Fuzz target for the full 3MF thumbnail pipeline (ZIP/OPC parsing, relationship resolution,
        // embedded-image decoding and mesh rendering). The provider is invoked on untrusted files by
        // Explorer, so this feeds arbitrary bytes as a candidate .3mf package. GetThumbnail is designed
        // to swallow malformed input and return null; libFuzzer still surfaces the failures that must
        // never happen here — hangs, stack overflows and memory-exhaustion (the reason the loader
        // enforces decompression / geometry / part-count budgets).
        public static void FuzzGetThumbnail(ReadOnlySpan<byte> input)
        {
            using var request = new FuzzRequest(input.ToArray());
            Requests.Add(request);
            request.Completed.Wait();
            if (request.Exception != null)
            {
                ExceptionDispatchInfo.Capture(request.Exception).Throw();
            }
        }

        private static Thread StartStaWorker()
        {
            var thread = new Thread(() =>
            {
                foreach (var request in Requests.GetConsumingEnumerable())
                {
                    try
                    {
                        using var stream = new MemoryStream(request.Input);
                        using var thumbnail = Provider.GetThumbnail(stream, 256);
                    }
                    catch (Exception ex)
                    {
                        request.Exception = ex;
                    }
                    finally
                    {
                        request.Completed.Set();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ThreeMfThumbnailProvider.FuzzTests.STA",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }

        private sealed class FuzzRequest : IDisposable
        {
            public FuzzRequest(byte[] input)
            {
                Input = input;
            }

            public byte[] Input { get; }

            public ManualResetEventSlim Completed { get; } = new();

            public Exception Exception { get; set; }

            public void Dispose()
            {
                Completed.Dispose();
            }
        }
    }
}
