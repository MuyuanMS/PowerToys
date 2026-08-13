// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace MouseWithoutBorders.Core;

internal sealed class FatalImpersonationException : Exception
{
    internal FatalImpersonationException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

internal static class ImpersonationHelper
{
    internal const int RevertToSelfAttempts = 3;
    internal const int RevertToSelfRetryDelayMilliseconds = 10;

    internal static bool TryRevertToSelf(Func<bool> revertToSelf, Action<int> delay)
    {
        ArgumentNullException.ThrowIfNull(revertToSelf);
        ArgumentNullException.ThrowIfNull(delay);

        for (int attempt = 0; attempt < RevertToSelfAttempts; attempt++)
        {
            if (revertToSelf())
            {
                return true;
            }

            if (attempt + 1 < RevertToSelfAttempts)
            {
                delay(RevertToSelfRetryDelayMilliseconds);
            }
        }

        return false;
    }

    internal static void RevertToSelfOrFailFast(
        Func<bool> revertToSelf,
        Action<int> delay,
        Action<string> failFast,
        Func<string> failureMessageFactory)
    {
        ArgumentNullException.ThrowIfNull(failFast);
        ArgumentNullException.ThrowIfNull(failureMessageFactory);

        try
        {
            if (TryRevertToSelf(revertToSelf, delay))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            FailFastAndThrow(failFast, CreateFailureMessage(failureMessageFactory, exception), exception);
        }

        FailFastAndThrow(failFast, CreateFailureMessage(failureMessageFactory));
    }

    private static string CreateFailureMessage(Func<string> failureMessageFactory, Exception cleanupException = null)
    {
        string failureMessage;
        try
        {
            failureMessage = failureMessageFactory();
        }
        catch (Exception exception)
        {
            failureMessage = $"Unable to restore the process identity. Failure message creation failed with {exception.GetType().Name}.";
        }

        return cleanupException is null
            ? failureMessage
            : string.Concat(failureMessage, " Cleanup failed with ", cleanupException.GetType().Name, ".");
    }

    private static void FailFastAndThrow(Action<string> failFast, string failureMessage, Exception innerException = null)
    {
        try
        {
            failFast(failureMessage);
        }
        catch (Exception exception)
        {
            throw new FatalImpersonationException(failureMessage, exception);
        }

        // Environment.FailFast never returns. This throw protects test hooks and
        // any future failure handler from allowing the impersonated thread to continue.
        throw new FatalImpersonationException(failureMessage, innerException);
    }
}
