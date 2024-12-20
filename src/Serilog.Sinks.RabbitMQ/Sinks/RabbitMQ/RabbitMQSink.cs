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
public sealed class RabbitMQSink : IBatchedLogEventSink, ILogEventSink, IDisposable
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
    private bool _disposedValue;

    internal RabbitMQSink(
        RabbitMQClientConfiguration rabbitMQClientConfiguration,
        RabbitMQSinkConfiguration rabbitMQSinkConfiguration,
        ILogEventSink? failureSink = null)
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
    /// Constructor for testing purposes.
    /// </summary>
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
    public void Emit(LogEvent logEvent) => AsyncHelpers.RunSync(() => EmitAsync(logEvent));

    /// <inheritdoc cref="IBatchedLogEventSink.EmitBatchAsync" />
    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        // make sure we have an array to avoid multiple enumeration
        var logEvents = batch as LogEvent[] ?? batch.ToArray();

        try
        {
            foreach (var logEvent in logEvents)
                await EmitAsync(logEvent).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!HandleException(exception, logEvents))
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
            _client.Close();
        }
        catch (Exception exception)
        {
            // ignored
            SelfLog.WriteLine("Exception occurred closing RabbitMQClient {0}", exception.Message);
        }

        // Dispose the failure sink if it's disposable.
        if (_failureSink is IDisposable disposableFailureSink)
        {
            disposableFailureSink.Dispose();
        }

        _client.Dispose();

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
    /// Handles the exceptions from <see cref="EmitBatchAsync"/>.
    /// </summary>
    /// <param name="ex">Occurred exception.</param>
    /// <param name="events">Batch of log events.</param>
    /// <returns>true when exception has been handled.</returns>
    private bool HandleException(Exception ex, LogEvent[] events)
    {
        if (_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.WriteToSelfLog))
        {
            // RabbitMQ returns an error, output the error to the SelfLog
            SelfLog.WriteLine("Caught exception while performing bulk operation to RabbitMQ: {0}", ex);
        }

        if (_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.WriteToFailureSink) && _failureSink != null)
        {
            // Send to a failure sink
            try
            {
                foreach (var e in events)
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

        // Return true if the exception has been handled. e.g. when the exception doesn't need to be rethrown.
        return !_emitEventFailureHandling.HasFlag(EmitEventFailureHandling.ThrowException);
    }
}
