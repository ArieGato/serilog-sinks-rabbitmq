// Copyright 2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Text;
using Microsoft.IO;
using RabbitMQ.Client;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Serilog RabbitMQ Sink - lets you log to RabbitMQ using Serilog.
/// </summary>
/// <remarks>
/// Publish failures always propagate. For the batched pipeline (<c>WriteTo.RabbitMQ(...)</c>)
/// the surrounding <c>BatchingSink</c> observes the exception and forwards the original batch
/// to its <see cref="ILoggingFailureListener"/> — this is what enables composition with
/// <c>WriteTo.FallbackChain(...)</c> / <c>WriteTo.Fallible(...)</c>. For the audit path
/// (<c>AuditTo.RabbitMQ(...)</c>) the exception surfaces to the caller; an
/// <see cref="ILoggingFailureListener"/> registered via <see cref="SetFailureListener"/>
/// is notified before the rethrow.
/// </remarks>
public sealed class RabbitMQSink : IBatchedLogEventSink, ILogEventSink, ISetLoggingFailureListener, IDisposable
{
    private static readonly RecyclableMemoryStreamManager _manager = new();
    private static readonly Encoding _utf8NoBOM = new UTF8Encoding(false);

    private readonly ITextFormatter _formatter;
    private readonly IRabbitMQClient _client;
    private readonly ISendMessageEvents _sendMessageEvents;
    private readonly bool _persistent;
    private readonly string _routingKey;
    private ILoggingFailureListener? _failureListener;
    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQSink"/> class.
    /// </summary>
    /// <param name="rabbitMQClientConfiguration">The RabbitMQ client configuration.</param>
    /// <param name="rabbitMQSinkConfiguration">The sink configuration.</param>
    internal RabbitMQSink(
        RabbitMQClientConfiguration rabbitMQClientConfiguration,
        RabbitMQSinkConfiguration rabbitMQSinkConfiguration)
    {
        _formatter = rabbitMQSinkConfiguration.TextFormatter;
        _client = new RabbitMQClient(rabbitMQClientConfiguration);
        _sendMessageEvents = rabbitMQClientConfiguration.SendMessageEvents ??
                             new SendMessageEvents();

        _persistent = rabbitMQClientConfiguration.DeliveryMode == RabbitMQDeliveryMode.Durable;
        _routingKey = rabbitMQClientConfiguration.RoutingKey;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQSink"/> class. Used for testing purposes.
    /// </summary>
    /// <param name="client">The RabbitMQ client used to publish events.</param>
    /// <param name="textFormatter">The text formatter used to render log events.</param>
    /// <param name="sendMessageEvents">Hooks for customising message properties and routing key.</param>
    /// <param name="persistent">Whether messages should be marked persistent.</param>
    /// <param name="routingKey">The default routing key.</param>
    internal RabbitMQSink(
        IRabbitMQClient client,
        ITextFormatter textFormatter,
        ISendMessageEvents sendMessageEvents,
        bool persistent = false,
        string routingKey = "")
    {
        _client = client;
        _formatter = textFormatter;
        _sendMessageEvents = sendMessageEvents;
        _persistent = persistent;
        _routingKey = routingKey;
    }

    /// <inheritdoc cref="ILogEventSink.Emit" />
    public void Emit(LogEvent logEvent)
    {
        try
        {
            AsyncHelpers.RunSync(() => EmitAsync(logEvent));
        }
        catch (Exception ex) when (_failureListener is not null)
        {
            // The `when (_failureListener is not null)` filter above means this catch frame
            // is not entered at all when no listener is registered — the exception propagates
            // with its original stack intact, and we avoid the LogEvent[] allocation on the
            // hot no-listener path.
            //
            // Audit path: notify the listener before propagating so a Fallback-wrapped audit
            // pipeline sees the failed event. The `throw;` below preserves the original stack.
            NotifyListener(new[] { logEvent }, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public void SetFailureListener(ILoggingFailureListener failureListener)
    {
        // Per ISetLoggingFailureListener contract this is called once during
        // initialization on the init thread, before logging starts. Plain field
        // assignment is sufficient; no synchronisation required.
        //
        // Note: for the batched pipeline (WriteTo.RabbitMQ) Serilog's BatchingSink
        // receives the listener and does not forward it — so _failureListener stays
        // null there and the native BatchingSink handling does the work. This matters
        // for the audit path (AuditTo.RabbitMQ) and for direct-construct users.
        _failureListener = failureListener;
    }

    /// <inheritdoc cref="IBatchedLogEventSink.EmitBatchAsync" />
    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        // Publish each event in order. On failure we let the exception propagate so
        // the surrounding BatchingSink can hand the original batch to its
        // ILoggingFailureListener — that listener is what WriteTo.FallbackChain
        // and WriteTo.Fallible hook into. Events that already published before the
        // failure remain on the broker; the failing event and the rest of the batch
        // are re-emitted by the fallback chain (downstream sinks should be idempotent
        // on MessageId if duplicates matter).
        var logEvents = batch as LogEvent[] ?? batch.ToArray();
        try
        {
            foreach (var logEvent in logEvents)
            {
                await EmitAsync(logEvent).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("Caught exception while performing bulk operation to RabbitMQ: {0}", ex);
            throw;
        }
    }

    /// <inheritdoc cref="IBatchedLogEventSink.OnEmptyBatchAsync" />
    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        if (_disposedValue)
            return;

        try
        {
            // Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods.
            // https://www.rabbitmq.com/dotnet-api-guide.html#disconnecting
            // Bridge to async disposal here — this is the sync entry point Serilog invokes.
            AsyncHelpers.RunSync(() => _client.DisposeAsync().AsTask());
        }
        catch (Exception exception)
        {
            // Route to both SelfLog and Trace. SelfLog is the canonical diagnostics
            // channel for Serilog, but callers who never wire a SelfLog listener would
            // otherwise lose shutdown diagnostics silently. Trace output surfaces in
            // debugger output / ETW without any opt-in (issue #286 item 3).
            SelfLog.WriteLine("Exception occurred while disposing RabbitMQClient {0}", exception.Message);
            System.Diagnostics.Trace.TraceError("Exception occurred while disposing RabbitMQClient {0}", exception.Message);
        }

        _disposedValue = true;
    }

    /// <summary>
    /// Emits a log event to RabbitMQ.
    /// </summary>
    /// <param name="logEvent">The log event to write.</param>
    private Task EmitAsync(LogEvent logEvent)
    {
        using var stream = _manager.GetStream();
        using var sw = new StreamWriter(stream, _utf8NoBOM);
        _formatter.Format(logEvent, sw);
        sw.Flush();

        var basicProperties = new BasicProperties { Persistent = _persistent };
        _sendMessageEvents.OnSetMessageProperties(logEvent, basicProperties);

        string routingKey = _sendMessageEvents.OnGetRoutingKey(logEvent, _routingKey);
        return _client.PublishAsync(new ReadOnlyMemory<byte>(stream.GetBuffer(), 0, (int)stream.Length), basicProperties, routingKey);
    }

    /// <summary>
    /// Notifies the Serilog 4.1+ <see cref="ILoggingFailureListener"/> that a publish has
    /// failed. Only meaningful for the sync <see cref="Emit(LogEvent)"/> audit path — the
    /// batched pipeline routes failures through <c>BatchingSink</c>'s own listener, which
    /// <see cref="ISetLoggingFailureListener"/> does not forward to inner sinks.
    /// </summary>
    /// <param name="events">Log events associated with the failure.</param>
    /// <param name="ex">Original publish exception.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A throwing failure listener must not recurse or break the sink; swallow and log to SelfLog. The original publish exception is propagated by the caller.")]
    private void NotifyListener(IReadOnlyCollection<LogEvent> events, Exception ex)
    {
        try
        {
            _failureListener!.OnLoggingFailed(
                this,
                LoggingFailureKind.Permanent,
                "RabbitMQ publish failed.",
                events,
                ex);
        }
        catch (Exception exListener)
        {
            // A throwing listener must not recurse; drop to SelfLog and continue.
            SelfLog.WriteLine("Failure listener threw: {0}", exListener);
        }
    }
}
