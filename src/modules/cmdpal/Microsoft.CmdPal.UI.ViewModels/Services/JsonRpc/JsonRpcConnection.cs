// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ManagedCommon;
using StreamJsonRpc;

namespace Microsoft.CmdPal.UI.ViewModels.Services.JsonRpc;

/// <summary>
/// Adapts StreamJsonRpc to the raw JSON contract used by JavaScript extensions.
/// </summary>
public sealed class JsonRpcConnection : IDisposable
{
    private const int NotificationQueueCapacity = 1024;
    internal const int InboundRequestConcurrencyLimit = 16;
    internal const int MaxInboundContentLength = 32 * 1024 * 1024;
    internal const int MaxQueuedNotificationBytes = 8 * 1024 * 1024;

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly CmdPalJsonRpc _rpc;
    private readonly IJsonRpcMessageHandler _messageHandler;
    private readonly IJsonRpcMessageFactory _messageFactory;
    private readonly Stream? _errorStream;
    private readonly TimeSpan _requestTimeout;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly ConcurrentDictionary<string, Action<JsonElement>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<JsonElement, CancellationToken, Task<JsonNode?>>> _requestHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RpcMethodTarget> _registeredMethods = new(StringComparer.Ordinal);
    private readonly object _registrationLock = new();
    private readonly SemaphoreSlim _inboundRequestGate = new(InboundRequestConcurrencyLimit, InboundRequestConcurrencyLimit);
    private readonly Channel<NotificationEnvelope> _notificationQueue = Channel.CreateBounded<NotificationEnvelope>(
        new BoundedChannelOptions(NotificationQueueCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private Task? _notificationConsumerTask;
    private Task? _errorPumpTask;
    private int _started;
    private int _disposed;
    private int _disconnectedRaised;
    private int _nextRequestId;
    private long _droppedNotifications;
    private long _queuedNotificationBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcConnection"/> class.
    /// </summary>
    /// <param name="input">The stream carrying messages from the extension.</param>
    /// <param name="output">The stream carrying messages to the extension.</param>
    /// <param name="errorStream">An optional stream carrying extension diagnostics.</param>
    /// <param name="requestTimeout">The timeout applied to requests.</param>
    public JsonRpcConnection(Stream input, Stream output, Stream? errorStream = null, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        _errorStream = errorStream;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (_requestTimeout < TimeSpan.Zero && _requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be non-negative or infinite.");
        }

        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions =
            {
                TypeInfoResolver = JsonRpcSerializerContext.Default,
            },
        };
        _messageFactory = formatter;

        _messageHandler = new HeaderDelimitedMessageHandler(output, new InboundFrameLimitStream(input), formatter);
        _rpc = new CmdPalJsonRpc(_messageHandler)
        {
            AllowModificationWhileListening = true,
            CancelLocallyInvokedMethodsWhenConnectionIsClosed = true,
            SynchronizationContext = null,
        };

        _rpc.Disconnected += OnRpcDisconnected;
    }

    /// <summary>
    /// Raised when the underlying connection closes.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Raised when the connection encounters a protocol or handler error.
    /// </summary>
    public event EventHandler<JsonRpcErrorEventArgs>? Error;

    internal long DroppedNotificationCount => Interlocked.Read(ref _droppedNotifications);

    internal long QueuedNotificationBytes => Interlocked.Read(ref _queuedNotificationBytes);

    internal Task NotificationConsumerCompletion => _notificationConsumerTask ?? Task.CompletedTask;

    /// <summary>
    /// Starts listening for messages from the extension.
    /// </summary>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The connection is already listening.");
        }

        _notificationConsumerTask = Task.Run(ConsumeNotificationsAsync);
        if (_errorStream is not null)
        {
            _errorPumpTask = Task.Run(PumpErrorStreamAsync);
        }

        _rpc.StartListening();
    }

    /// <summary>
    /// Sends a request and returns its raw result or error.
    /// </summary>
    /// <param name="method">The remote method name.</param>
    /// <param name="parameters">The parameter object.</param>
    /// <param name="cancellationToken">A token that cancels the local wait.</param>
    /// <returns>The request result or error.</returns>
    public async Task<JsonRpcResponse> SendRequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrEmpty(method);

        var requestId = Interlocked.Increment(ref _nextRequestId);
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectionClosedCts.Token);
        try
        {
            var invokeTask = _rpc.InvokeWithParameterObjectAsync<JsonElement>(requestId, method, parameters, requestCts.Token);
            var result = await invokeTask.WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);

            return new JsonRpcResponse
            {
                Id = requestId,
                Result = result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : result,
            };
        }
        catch (ConnectionLostException ex)
        {
            throw new JsonRpcException("The JSON-RPC connection closed before a response was received.", ex);
        }
        catch (RemoteRpcException ex)
        {
            return new JsonRpcResponse
            {
                Id = requestId,
                Error = new JsonRpcError
                {
                    Code = GetErrorCode(ex),
                    Message = ex.Message,
                    Data = GetErrorData(ex),
                },
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            requestCts.Cancel();
            throw;
        }
        catch (OperationCanceledException) when (_connectionClosedCts.IsCancellationRequested)
        {
            throw new JsonRpcException("The JSON-RPC connection closed before a response was received.");
        }
        catch (TimeoutException)
        {
            requestCts.Cancel();
            throw;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException)
        {
            throw new JsonRpcException("The JSON-RPC connection closed before a response was received.", ex);
        }
    }

    /// <summary>
    /// Sends a request and deserializes its successful result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="method">The remote method name.</param>
    /// <param name="parameters">The parameter object.</param>
    /// <param name="resultTypeInfo">Source generated metadata for the result.</param>
    /// <param name="cancellationToken">A token that cancels the local wait.</param>
    /// <returns>The deserialized result.</returns>
    public async Task<TResult?> SendRequestAsync<TResult>(string method, JsonNode? parameters, JsonTypeInfo<TResult> resultTypeInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resultTypeInfo);

        var response = await SendRequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        if (response.Error is not null)
        {
            throw new JsonRpcException(response.Error);
        }

        if (response.Result is not { } result)
        {
            return default;
        }

        return result.Deserialize(resultTypeInfo);
    }

    /// <summary>
    /// Sends a notification to the extension.
    /// </summary>
    /// <param name="method">The remote method name.</param>
    /// <param name="parameters">The parameter object.</param>
    /// <param name="cancellationToken">A token that cancels the send before it starts.</param>
    /// <returns>A task that completes when the notification is sent.</returns>
    public async Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrEmpty(method);

        try
        {
            var notification = _messageFactory.CreateRequestMessage();
            notification.Method = method;
            notification.Arguments = parameters;
            await _messageHandler.WriteAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException or IOException or TimeoutException)
        {
            DisposeRpc();
            throw new JsonRpcException("The JSON-RPC connection failed while writing a notification.", ex);
        }
    }

    /// <summary>
    /// Registers a handler for an inbound notification.
    /// </summary>
    /// <param name="method">The notification method name.</param>
    /// <param name="handler">The handler to invoke.</param>
    public void RegisterNotificationHandler(string method, Action<JsonElement> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);

        _notificationHandlers[method] = handler;
        RegisterMethod(method);
    }

    /// <summary>
    /// Registers a handler for an inbound request.
    /// </summary>
    /// <param name="method">The request method name.</param>
    /// <param name="handler">The handler to invoke.</param>
    public void RegisterRequestHandler(string method, Func<JsonElement, CancellationToken, Task<JsonNode?>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);

        _requestHandlers[method] = handler;
        RegisterMethod(method);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _notificationQueue.Writer.TryComplete();
        _disposalCts.Cancel();
        _connectionClosedCts.Cancel();
        DisposeRpc();

        var tasks = new[]
        {
            _notificationConsumerTask ?? Task.CompletedTask,
            _errorPumpTask ?? Task.CompletedTask,
            _rpc.Completion,
        };
        try
        {
            Task.WhenAll(tasks).Wait(DisposeDrainTimeout);
        }
        catch (AggregateException)
        {
        }

        _ = DisposeTokenSourcesWhenTasksCompleteAsync(tasks, _disposalCts, _connectionClosedCts);
    }

    private static async Task DisposeTokenSourcesWhenTasksCompleteAsync(
        Task[] tasks,
        CancellationTokenSource disposalCts,
        CancellationTokenSource connectionClosedCts)
    {
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        disposalCts.Dispose();
        connectionClosedCts.Dispose();
    }

    private void RegisterMethod(string method)
    {
        lock (_registrationLock)
        {
            if (_registeredMethods.ContainsKey(method))
            {
                return;
            }

            var target = new RpcMethodTarget(this, method);
            _rpc.AddLocalRpcMethod(
                typeof(RpcMethodTarget).GetMethod(nameof(RpcMethodTarget.InvokeAsync))!,
                target,
                new JsonRpcMethodAttribute(method)
                {
                    UseSingleObjectParameterDeserialization = true,
                });
            _registeredMethods[method] = target;
        }
    }

    private async Task<JsonNode?> DispatchMethodAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (_rpc.IsResponseExpected)
        {
            if (_requestHandlers.TryGetValue(method, out var requestHandler))
            {
                if (!_inboundRequestGate.Wait(0, cancellationToken))
                {
                    throw new LocalRpcException("The JSON-RPC request concurrency limit was reached.")
                    {
                        ErrorCode = JsonRpcError.ServerBusy,
                    };
                }

                try
                {
                    return await requestHandler(parameters, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"The JSON-RPC request handler for '{method}' failed.", ex);
                    throw new LocalRpcException(ex.Message, ex)
                    {
                        ErrorCode = JsonRpcError.InternalError,
                    };
                }
                finally
                {
                    _inboundRequestGate.Release();
                }
            }

            throw new LocalRpcException($"No request handler is registered for '{method}'.")
            {
                ErrorCode = JsonRpcError.MethodNotFound,
            };
        }

        if (_notificationHandlers.ContainsKey(method))
        {
            EnqueueNotification(method, parameters);
        }

        return null;
    }

    private void EnqueueNotification(string method, JsonElement parameters)
    {
        var payloadBytes = parameters.ValueKind == JsonValueKind.Undefined
            ? 0
            : Encoding.UTF8.GetByteCount(parameters.GetRawText());

        if (payloadBytes > MaxQueuedNotificationBytes)
        {
            Interlocked.Increment(ref _droppedNotifications);
            return;
        }

        while (!TryReserveNotificationBytes(payloadBytes))
        {
            if (_notificationQueue.Reader.TryRead(out var dropped))
            {
                ReleaseNotificationBytes(dropped.PayloadBytes);
                Interlocked.Increment(ref _droppedNotifications);
                continue;
            }

            Interlocked.Increment(ref _droppedNotifications);
            return;
        }

        var clonedParameters = parameters.ValueKind == JsonValueKind.Undefined
            ? default
            : parameters.Clone();
        var envelope = new NotificationEnvelope(method, clonedParameters, payloadBytes);
        while (!_notificationQueue.Writer.TryWrite(envelope))
        {
            if (_notificationQueue.Reader.TryRead(out var dropped))
            {
                ReleaseNotificationBytes(dropped.PayloadBytes);
                Interlocked.Increment(ref _droppedNotifications);
                continue;
            }

            ReleaseNotificationBytes(payloadBytes);
            Interlocked.Increment(ref _droppedNotifications);
            return;
        }
    }

    private bool TryReserveNotificationBytes(int payloadBytes)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _queuedNotificationBytes);
            if (payloadBytes > MaxQueuedNotificationBytes - current)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _queuedNotificationBytes, current + payloadBytes, current) == current)
            {
                return true;
            }
        }
    }

    private void ReleaseNotificationBytes(int payloadBytes)
    {
        Interlocked.Add(ref _queuedNotificationBytes, -payloadBytes);
    }

    private async Task ConsumeNotificationsAsync()
    {
        try
        {
            while (await _notificationQueue.Reader.WaitToReadAsync(_disposalCts.Token).ConfigureAwait(false))
            {
                while (_notificationQueue.Reader.TryRead(out var envelope))
                {
                    ReleaseNotificationBytes(envelope.PayloadBytes);
                    if (!_notificationHandlers.TryGetValue(envelope.Method, out var handler))
                    {
                        continue;
                    }

                    try
                    {
                        handler(envelope.Parameters);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"The JSON-RPC notification handler for '{envelope.Method}' failed.", ex);
                        RaiseError(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError("The JSON-RPC notification pump ended unexpectedly.", ex);
            RaiseError(ex);
        }
    }

    private async Task PumpErrorStreamAsync()
    {
        try
        {
            var reader = new BoundedStderrReader(line => Logger.LogWarning($"[extension stderr] {line}"));
            await reader.PumpAsync(_errorStream!, _disposalCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"The JSON-RPC stderr pump ended: {ex.Message}");
        }
    }

    private void OnRpcDisconnected(object? sender, JsonRpcDisconnectedEventArgs e)
    {
        _connectionClosedCts.Cancel();

        if (e.Exception is not null && e.Reason is not DisconnectedReason.LocallyDisposed and not DisconnectedReason.RemotePartyTerminated)
        {
            RaiseError(e.Exception);
        }

        if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisposeRpc()
    {
        try
        {
            _rpc.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RaiseError(Exception exception)
    {
        var handlers = Error;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new JsonRpcErrorEventArgs(exception);
        foreach (EventHandler<JsonRpcErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception ex)
            {
                Logger.LogError("A JSON-RPC error event handler failed.", ex);
            }
        }
    }

    private static int GetErrorCode(RemoteRpcException exception)
    {
        return exception switch
        {
            RemoteInvocationException invocationException => invocationException.ErrorCode,
            _ when exception.ErrorCode is { } code => (int)code,
            _ => JsonRpcError.InternalError,
        };
    }

    private static JsonNode? GetErrorData(RemoteRpcException exception)
    {
        return exception.ErrorData switch
        {
            JsonElement element when element.ValueKind != JsonValueKind.Undefined => JsonNode.Parse(element.GetRawText()),
            JsonNode node => node.DeepClone(),
            _ => null,
        };
    }

    private sealed class RpcMethodTarget
    {
        private readonly JsonRpcConnection _connection;
        private readonly string _method;

        internal RpcMethodTarget(JsonRpcConnection connection, string method)
        {
            _connection = connection;
            _method = method;
        }

        public Task<JsonNode?> InvokeAsync(JsonElement parameters = default, CancellationToken cancellationToken = default)
        {
            return _connection.DispatchMethodAsync(_method, parameters, cancellationToken);
        }
    }

    internal sealed class InboundFrameLimitStream : Stream
    {
        private const int MaxHeaderLength = 8 * 1024;

        private readonly Stream _inner;
        private readonly byte[] _header = new byte[MaxHeaderLength];
        private int _headerLength;
        private long _remainingBodyBytes;

        internal InboundFrameLimitStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Validate(buffer.AsSpan(offset, read));
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            Validate(buffer.AsSpan(offset, read));
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Validate(buffer.Span[..read]);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Validate(ReadOnlySpan<byte> bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                if (_remainingBodyBytes > 0)
                {
                    var consumed = Math.Min(_remainingBodyBytes, bytes.Length - offset);
                    _remainingBodyBytes -= consumed;
                    offset += (int)consumed;
                    continue;
                }

                if (_headerLength == MaxHeaderLength)
                {
                    throw new InvalidDataException($"The JSON-RPC header exceeds {MaxHeaderLength} bytes.");
                }

                _header[_headerLength++] = bytes[offset++];
                if (_headerLength >= 4 &&
                    _header[_headerLength - 4] == (byte)'\r' &&
                    _header[_headerLength - 3] == (byte)'\n' &&
                    _header[_headerLength - 2] == (byte)'\r' &&
                    _header[_headerLength - 1] == (byte)'\n')
                {
                    _remainingBodyBytes = ParseContentLength(_header.AsSpan(0, _headerLength));
                    _headerLength = 0;
                }
            }
        }

        private static long ParseContentLength(ReadOnlySpan<byte> headerBytes)
        {
            var header = Encoding.ASCII.GetString(headerBytes);
            long? contentLength = null;
            foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0 ||
                    !line.AsSpan(0, separator).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (contentLength is not null ||
                    !long.TryParse(line.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                    parsed < 0)
                {
                    throw new InvalidDataException("The JSON-RPC Content-Length header is invalid.");
                }

                contentLength = parsed;
            }

            if (contentLength is null)
            {
                throw new InvalidDataException("The JSON-RPC header does not contain Content-Length.");
            }

            if (contentLength > MaxInboundContentLength)
            {
                throw new InvalidDataException($"The JSON-RPC Content-Length exceeds the {MaxInboundContentLength}-byte limit.");
            }

            return contentLength.Value;
        }
    }

    private sealed class CmdPalJsonRpc : StreamJsonRpc.JsonRpc
    {
        private readonly AsyncLocal<bool?> _isResponseExpected = new();

        internal CmdPalJsonRpc(IJsonRpcMessageHandler messageHandler)
            : base(messageHandler)
        {
        }

        internal bool IsResponseExpected => _isResponseExpected.Value ?? true;

        internal Task<TResult> InvokeWithParameterObjectAsync<TResult>(
            long requestId,
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            return InvokeCoreAsync<TResult>(
                new RequestId(requestId),
                method,
                parameters is null ? null : new object[] { parameters },
                positionalArgumentDeclaredTypes: null,
                namedArgumentDeclaredTypes: null,
                cancellationToken,
                isParameterObject: true);
        }

        protected override async ValueTask<StreamJsonRpc.Protocol.JsonRpcMessage> DispatchRequestAsync(
            StreamJsonRpc.Protocol.JsonRpcRequest request,
            TargetMethod targetMethod,
            CancellationToken cancellationToken)
        {
            var previousValue = _isResponseExpected.Value;
            _isResponseExpected.Value = request.IsResponseExpected;
            try
            {
                return await base.DispatchRequestAsync(request, targetMethod, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _isResponseExpected.Value = previousValue;
            }
        }

        protected override Type? GetErrorDetailsDataType(StreamJsonRpc.Protocol.JsonRpcError error) => typeof(JsonElement);
    }

    private readonly record struct NotificationEnvelope(string Method, JsonElement Parameters, int PayloadBytes);
}
