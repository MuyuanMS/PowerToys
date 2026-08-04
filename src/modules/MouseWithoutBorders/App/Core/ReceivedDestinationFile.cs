// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace MouseWithoutBorders.Core;

internal sealed class ReceivedDestinationFile : IDisposable
{
    private readonly Action<string> deleteFile;
    private readonly string path;
    private bool completed;

    internal ReceivedDestinationFile(string path, Action<string> deleteFile)
    {
        this.path = path;
        this.deleteFile = deleteFile;

        try
        {
            Stream = new FileStream(path, FileMode.CreateNew);
            DeleteOnFailure = true;
        }
        catch (IOException)
        {
            Stream = new FileStream(path, FileMode.Create);
        }
    }

    internal FileStream Stream { get; }

    internal bool DeleteOnFailure { get; }

    internal void Complete()
    {
        Stream.Flush();
        completed = true;
    }

    public void Dispose()
    {
        Stream.Dispose();

        if (DeleteOnFailure && !completed)
        {
            deleteFile(path);
        }
    }
}
