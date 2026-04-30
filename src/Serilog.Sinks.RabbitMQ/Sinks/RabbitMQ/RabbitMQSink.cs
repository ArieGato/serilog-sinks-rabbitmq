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
public sealed class RabbitMQSink : IBatchedLogEventSink, ILogEventSink, ISetLoggingFailureListener, IDisposable
{
    private static readonly RecyclableMemoryStreamManager _manager = new();
    private static readonly Encoding _utf8NoBOM = new UTF8Encoding(false);

    private readonly ITextFormatter _formatter;
    private readonly IRabbitMQClient _client;
    private readonly ILogEventSink? _failureSink;
    private readonly EmitEventFailureHandling _emitEventFailureHandling;
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
    /// <param name="failureSink">Optional sink that receives events when emission fails.</param>
    internal RabbitMQSink(
        RabbitMQClientConfiguration rabbitMQClientConfiguration,
        RabbitMQSinkConfiguration rabbitMQSinkConfiguration,
        ILogEventSink? failureSink)
    {
        _formatter = rabbitMQSinkConfiguration.TextFormatter;
        _client = new RabbitMQClient(rabbitMQClientConfiguration);
        _emitEventFailureHandling = rabbitMQSinkConfiguration.EmitEventFailure;
        _sendMessageEvents = rabbitMQClientConfiguration.SendMessageEvents ??
                             new SendMessageEvents();

        _failureSink = failureSink;
        _persistent = rabbitMQClientConfiguration.DeliveryMode == RabbitMQDeliveryMode.Durable;
        _routingKey = rabbitMQClientConfiguration.RoutingKey;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQSink"/> class. Used for testing purposes.
    /// </summary>
    /// <param name="client">The RabbitMQ client used to publish events.</param>
    /// <param name="textFormatter">The text formatter used to render log events.</param>
    /// <param name="sendMessageEvents">Hooks for customising message properties and routing key.</param>
    /// <param name="emitEventFailureHandling">How to handle failures when emitting events.</param>
    /// <param name="failureSink">Optional sink that receives events when emission fails.</param>
    /// <param name="persistent">Whether messages should be marked persistent.</param>
    /// <param name="routingKey">The default routing key.</param>
    internal RabbitMQSink(
        IRabbitMQClient client,
        ITextFormatter textFormatter,
        ISendMessageEvents sendMessageEvents,
        EmitEventFailureHandling emitEventFailureHandling = EmitEventFailureHandling.Ignore,
        ILogEventSink? failureSink = null,
        bool persistent = false,
        string routingKey = "")
    {
        _client = client;
        _formatter = textFormatter;
        _emitEventFailureHandling = emitEventFailureHandling;
        _sendMessageEvents = sendMessageEvents;
        _failureSink = failureSink;
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
        // make sure we have an array to avoid multiple enumeration
        var logEvents = batch as LogEvent[] ?? batch.ToArray();

        // Track the index we are about to publish so the catch can forward only the
        // un-published tail to the in-sink failure sink. Forwarding the entire batch
        // would duplicate every event that already published successfully (the broker
        // accepted them, then a later event in the same batch failed) — downstream
        // systems without idempotency on MessageId would see those duplicates.
        //
        // Important scope: this slicing only reaches the configured `failureSinkConfiguration`
        // sink (consumed inside HandleException). On the rethrow path Serilog's BatchingSink
        // hands its OWN copy of the original batch to its failure listener, so wrappers like
        // `WriteTo.Fallback(...)` still observe all M events, not the tail. Surfacing the
        // slice through BatchingSink would require an upstream contract change — see #318.
        int published = 0;
        try
        {
            for (; published < logEvents.Length; published++)
            {
                await EmitAsync(logEvents[published]).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // logEvents[published] is the failing event; the slice [published..] is
            // failing-plus-remainder, all of which are unpublished from the broker's
            // perspective. logEvents[..published] succeeded and must not be re-emitted.
            var unpublished = published == 0
                ? logEvents
                : logEvents.AsSpan(published).ToArray();

            if (!HandleException(exception, unpublished))
            {
                throw;
            }
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

        // Dispose the failure sink if it's disposable.
        if (_failureSink is IDisposable disposableFailureSink)
        {
            disposableFailureSink.Dispose();
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
    /// Handles the exceptions from <see cref="EmitBatchAsync"/>. Routes the failure to
    /// SelfLog and/or the in-sink <c>failureSinkConfiguration</c> failure sink according
    /// to <see cref="EmitEventFailureHandling"/>, and decides whether to swallow or
    /// rethrow.
    /// </summary>
    /// <param name="ex">The exception thrown by <see cref="EmitAsync"/>.</param>
    /// <param name="unpublishedEvents">
    /// The failing event followed by every event in the batch that came after it — the
    /// suffix that the broker did not accept. Events that already published successfully
    /// in the same batch are not in this slice and must not be re-emitted to the
    /// in-sink failure sink.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the exception has been fully handled and must not be
    /// rethrown (legacy catch-and-route via <see cref="EmitEventFailureHandling.WriteToFailureSink"/>
    /// without <see cref="EmitEventFailureHandling.ThrowException"/>). <see langword="false"/>
    /// when the caller should rethrow.
    ///
    /// On rethrow Serilog's <see cref="IBatchedLogEventSink"/> pipeline (typically
    /// <c>BatchingSink</c>) observes the failure and routes it through its own listener
    /// machinery — this is how <c>WriteTo.Fallback(...)</c> composes. Note that the
    /// listener receives the ORIGINAL batch <c>BatchingSink</c> passed in, not the
    /// <paramref name="unpublishedEvents"/> slice; the slicing only narrows what reaches
    /// the in-sink failure sink. See issue #318.
    /// </returns>
    private bool HandleException(Exception ex, LogEvent[] unpublishedEvents)
    {
        if (_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.WriteToSelfLog))
        {
            // RabbitMQ returns an error, output the error to the SelfLog
            SelfLog.WriteLine("Caught exception while performing bulk operation to RabbitMQ: {0}", ex);
        }

        if (_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.WriteToFailureSink) && _failureSink != null)
        {
            // Send the un-published tail to the failure sink. Events that already
            // published successfully in this batch are NOT in unpublishedEvents — they
            // were sliced out in EmitBatchAsync's catch and must not be re-emitted.
            try
            {
                foreach (var e in unpublishedEvents)
                {
                    _failureSink.Emit(e);
                }
            }
            catch (Exception exSink)
            {
                // No exception is thrown to the caller
                SelfLog.WriteLine("Caught exception while emitting to failure sink {0}: {1}", _failureSink, exSink.Message);
                SelfLog.WriteLine("Failure sink exception: {0}", exSink);
                SelfLog.WriteLine("Original exception: {0}", ex);
            }
        }

        // Rethrow unless the user has opted into the legacy catch-and-route path
        // (WriteToFailureSink) without also forcing ThrowException. Default — and every
        // combination that does not set WriteToFailureSink — propagates the exception so
        // BatchingSink's listener plumbing observes it. WriteToFailureSink + ThrowException
        // is the explicit "route to failure sink AND still throw" combination.
        bool legacyCatchOnly = _emitEventFailureHandling.HasFlag(EmitEventFailureHandling.WriteToFailureSink)
                            && !_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.ThrowException);
        return legacyCatchOnly;
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
