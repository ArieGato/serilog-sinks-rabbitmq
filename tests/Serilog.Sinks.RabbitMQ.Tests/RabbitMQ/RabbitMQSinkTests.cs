using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Sinks.RabbitMQ.Tests.TestHelpers;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

[Collection("SelfLog")]
public class RabbitMQSinkTests
{
    private sealed class StubClient : IRabbitMQClient
    {
        public Task PublishAsync(ReadOnlyMemory<byte> message, BasicProperties basicProperties, string? routingKey = null)
        {
            // Need to be stored as string because underlying array of ReadOnlyMemory is reused.
            Messages.Add(Encoding.UTF8.GetString(message.ToArray()));

            return Task.CompletedTask;
        }

        public Task CloseAsync() => throw new NotImplementedException();

        public ValueTask DisposeAsync() => throw new NotImplementedException();

        public List<string> Messages { get; } = [];
    }

    [Fact]
    public void Emit_ShouldPublishMessages()
    {
        // Arrange
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message", []), []);

        var textFormatter = Substitute.For<ITextFormatter>();
        textFormatter
            .When(x => x.Format(Arg.Any<LogEvent>(), Arg.Any<TextWriter>()))
            .Do(x => x.Arg<TextWriter>().Write(x.Arg<LogEvent>().MessageTemplate.Text));

        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = new StubClient();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Emit(logEvent);

        // Assert
        rabbitMQClient.Messages.Count.ShouldBe(1);
        rabbitMQClient.Messages[0].ShouldBe("some-message");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldPublishMessages()
    {
        // Arrange
        IReadOnlyCollection<LogEvent> logEvents = [
            new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message-1", []), []),
            new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message-2", []), [])];

        var textFormatter = Substitute.For<ITextFormatter>();
        textFormatter
            .When(x => x.Format(Arg.Any<LogEvent>(), Arg.Any<TextWriter>()))
            .Do(x => x.Arg<TextWriter>().Write(x.Arg<LogEvent>().MessageTemplate.Text));

        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = new StubClient();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        await sut.EmitBatchAsync(logEvents);

        // Assert
        rabbitMQClient.Messages.Count.ShouldBe(2);
        rabbitMQClient.Messages[0].ShouldBe("some-message-1");
        rabbitMQClient.Messages[1].ShouldBe("some-message-2");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldDoNothing_WhenNoEventsAreEmitted()
    {
        // Arrange
        IReadOnlyCollection<LogEvent> logEvents = [];

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        await sut.EmitBatchAsync(logEvents);

        // Assert
        await rabbitMQClient.DidNotReceive().PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>());
    }

    [Fact]
    public async Task OnEmptyBatchAsync_ReturnsCompletedTask_AndDoesNotTouchClient()
    {
        // OnEmptyBatchAsync is part of IBatchedLogEventSink and fires when Serilog's
        // BatchingSink has no events to emit — it must be a cheap no-op that neither
        // publishes nor allocates a broker round-trip. Assert both: the returned Task
        // is already completed (synchronous no-op) and the client sees no interaction.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        var task = sut.OnEmptyBatchAsync();

        // RanToCompletion is the portable equivalent of IsCompletedSuccessfully
        // (which is .NET Core+ only, and this test assembly also targets net48).
        task.Status.ShouldBe(TaskStatus.RanToCompletion);
        await task;
        await rabbitMQClient.DidNotReceive().PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Dispose_ShouldDisposeRabbitMQClient()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Dispose();

        // Assert
        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ShouldNotThrowException_WhenCalledTwice()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Dispose();
        sut.Dispose();

        // Assert
        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ShouldNotThrowException_WhenRabbitMQClientDisposeThrowsException()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.DisposeAsync())
            .Do(_ => throw new Exception("some-message"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Dispose();

        // Assert
        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public void Dispose_WritesToTraceError_WhenRabbitMQClientDisposeThrowsException()
    {
        // Item 3 from #286: Dispose() routes disposal exceptions to SelfLog, but
        // callers who have not opted into SelfLog lose diagnostics silently. Also
        // emit to System.Diagnostics.Trace so messages surface in debugger output
        // / ETW without any explicit opt-in. Trace.Listeners is process-global; the
        // [Collection("SelfLog")] attribute on this class serialises against other
        // tests that mutate similar global diagnostics state.
        using var listener = new StringBuilderTraceListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            var textFormatter = Substitute.For<ITextFormatter>();
            var messageEvents = Substitute.For<ISendMessageEvents>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.DisposeAsync())
                .Do(_ => throw new Exception("trace-boom"));

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

            sut.Dispose();

            listener.Output.ShouldContain("trace-boom");
            listener.Output.ShouldContain("RabbitMQClient");
        }
        finally
        {
            // Must detach from the process-global Trace.Listeners; `using` only
            // handles Dispose, not removal from the collection.
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }
    }

    private sealed class StringBuilderTraceListener : System.Diagnostics.TraceListener
    {
        private readonly StringBuilder _output = new();

        public string Output => _output.ToString();

        public override void Write(string? message) => _output.Append(message);

        public override void WriteLine(string? message) => _output.AppendLine(message);
    }

    [Fact]
    public void Dispose_ShouldNotDeadlock_WhenCalledOnSingleThreadedSynchronizationContext()
    {
        // AsyncHelpers.RunSync must fully isolate async continuations from the caller's
        // SynchronizationContext, otherwise Dispose() will deadlock under a single-threaded
        // UI-style context (WinForms/WPF). We install an outer context whose Post throws —
        // any continuation routed through it is a bug in the sync-over-async bridge, and
        // Sink.Dispose swallows exceptions into SelfLog so we also capture SelfLog to
        // detect silent regressions where the outer context got invoked. The
        // [Collection("SelfLog")] attribute on this class serialises every SelfLog-touching
        // test (issue #282) so the assertion below is not racy against parallel writers.
        using var selfLog = new SelfLogScope(out var selfLogBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = new YieldingDisposeClient();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        var disposeThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new ThrowingSynchronizationContext());
            sut.Dispose();
        })
        {
            IsBackground = true,
        };
        disposeThread.Start();

        disposeThread.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue(
            "RabbitMQSink.Dispose deadlocked on a single-threaded SynchronizationContext.");
        selfLogBuilder.ToString().ShouldBeEmpty();
        rabbitMQClient.DisposeAsyncCallCount.ShouldBe(1);
    }

    private sealed class YieldingDisposeClient : IRabbitMQClient
    {
        public int DisposeAsyncCallCount { get; private set; }

        public Task PublishAsync(ReadOnlyMemory<byte> message, BasicProperties basicProperties, string? routingKey = null) => Task.CompletedTask;

        public Task CloseAsync() => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;

            // Task.Yield without ConfigureAwait(false) forces the continuation through the
            // captured SynchronizationContext.Current. RunSync must install its pumped
            // context before invoking DisposeAsync so the yield lands in the pump, not the
            // outer single-threaded context.
            await Task.Yield();
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
            => throw new InvalidOperationException(
                "Continuation posted to outer SynchronizationContext; AsyncHelpers.RunSync failed to isolate.");

        public override void Send(SendOrPostCallback d, object? state)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldRethrowOriginalException_AndWriteToSelfLog_OnPartialBatchFailure()
    {
        // Publish failures always propagate so BatchingSink (and any wrapping
        // WriteTo.Fallback / FallbackChain) can route the original batch through
        // its ILoggingFailureListener. The rethrown exception must be the ORIGINAL
        // instance — preserved via `throw;` so the stack walks back to the failing
        // PublishAsync. SelfLog records one diagnostic line containing the exception text.
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        var publishCalls = 0;
        var brokerFailure = new InvalidOperationException("broker-failure");
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call >= 3)
                {
                    throw brokerFailure;
                }

                return Task.CompletedTask;
            });

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        // Act
        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);

        // Assert — the original exception instance propagates (referential equality, not just
        // type/message match). `throw;` preserves the stack; `throw ex` would not.
        var thrown = await Should.ThrowAsync<InvalidOperationException>(act);
        thrown.ShouldBeSameAs(brokerFailure);

        // SelfLog received the diagnostic line.
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        selfLogStringBuilder.ToString().ShouldContain("broker-failure");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldRethrowAndWriteSelfLog_WhenAllPublishesFail()
    {
        // Whole-batch failure: every PublishAsync throws. The first failure aborts
        // the loop, the exception propagates to the BatchingSink listener, and
        // SelfLog gets the diagnostic line.
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException("publish-fail"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2]);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(act);
        thrown.Message.ShouldBe("publish-fail");
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        selfLogStringBuilder.ToString().ShouldContain("publish-fail");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldStopOnFirstFailure_AndNotPublishLaterEventsInBatch()
    {
        // Loop ordering: when PublishAsync throws on event N, events N+1..M must
        // NOT be sent to the broker. Confirms we do not silently continue past the
        // failure (which would publish out-of-order with the BatchingSink retry
        // surface and could double-publish on a Fallback re-emit).
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        var publishCalls = 0;
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call == 3)
                {
                    throw new InvalidOperationException("broker-failure");
                }

                return Task.CompletedTask;
            });

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);
        await Should.ThrowAsync<InvalidOperationException>(act);

        // 1, 2 succeeded; 3 threw; 4, 5 must not have been attempted.
        publishCalls.ShouldBe(3);
    }

    [Fact]
    public void Emit_Should_Use_RoutingKeyFunction_If_Specified()
    {
        // Arrange
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message", []), []);
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var channelPool = Substitute.For<IRabbitMQChannelPool>();
        var rabbitMQChannel = Substitute.For<IRabbitMQChannel>();
        channelPool.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IRabbitMQChannel>(rabbitMQChannel));

        // configure default send message events
        var messageEvents = new SendMessageEvents();

        var rabbitMQClient = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        var sut = new RabbitMQSink(rabbitMQClient, new CompactJsonFormatter(), messageEvents, routingKey: "some-route-key");

        // Act
        sut.Emit(logEvent);

        // Assert
        rabbitMQChannel.Received(1).BasicPublishAsync(Arg.Any<PublicationAddress>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>());
        rabbitMQChannel.ReceivedCalls().First().GetArguments()[0].ShouldBeOfType<PublicationAddress>().RoutingKey.ShouldBe("some-route-key");
    }

    [Fact]
    public void WriteTo_Should_Throw_If_Called_On_Null()
    {
        LoggerSinkConfiguration config = null!;
        Should.Throw<ArgumentNullException>(() => config.RabbitMQ((_, _) => { })).ParamName.ShouldBe("loggerSinkConfiguration");
    }

    [Fact]
    public void AuditTo_Should_Throw_If_Called_On_Null()
    {
        LoggerAuditSinkConfiguration config = null!;
        Should.Throw<ArgumentNullException>(() => config.RabbitMQ((_, _) => { })).ParamName.ShouldBe("loggerAuditSinkConfiguration");
    }

    private static IRabbitMQClient ClientThatFailsPublish(string message = "publish-fail")
    {
        var client = Substitute.For<IRabbitMQClient>();
        client.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException(message));
        return client;
    }

    private static RabbitMQSink CreateSut(IRabbitMQClient rabbitMQClient)
        => new(
            rabbitMQClient,
            Substitute.For<ITextFormatter>(),
            Substitute.For<ISendMessageEvents>());

    [Fact]
    public void Emit_Audit_NotifiesListenerAndRethrows_WhenPublishFails()
    {
        // Audit path: Fallback wrappers directly set the listener on RabbitMQSink (there
        // is no BatchingSink in between). Listener fires, exception still propagates so
        // audit semantics ("throw on failure") are preserved.
        var listener = Substitute.For<ILoggingFailureListener>();
        var sut = CreateSut(ClientThatFailsPublish());
        sut.SetFailureListener(listener);

        var logEvent = LogEventBuilder.Create().Build();
        Should.Throw<InvalidOperationException>(() => sut.Emit(logEvent));

        listener.Received(1).OnLoggingFailed(
            sut,
            LoggingFailureKind.Permanent,
            Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<LogEvent>>(e => e.Count == 1 && e.Single() == logEvent),
            Arg.Is<Exception>(e => e is InvalidOperationException && e.Message == "publish-fail"));
    }

    [Fact]
    public void Emit_Audit_RethrowsWithoutNotification_WhenListenerIsNotSet()
    {
        // Baseline: audit path without a listener still rethrows; no extra handling runs.
        var sut = CreateSut(ClientThatFailsPublish());

        Should.Throw<InvalidOperationException>(() => sut.Emit(LogEventBuilder.Create().Build()));
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task DisposeAsync_DisposesRabbitMQClient()
    {
        // The async-dispose path is preferred over Dispose() inside async pipelines:
        // BatchingSink forwards DisposeAsync to its target sink when the sink implements
        // IAsyncDisposable. Our DisposeAsync awaits IRabbitMQClient.DisposeAsync directly
        // — no AsyncHelpers.RunSync bridge — so this assertion is the single behavioural
        // difference vs Dispose().
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        await sut.DisposeAsync();

        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Double-DisposeAsync must not throw and must hit the underlying client once —
        // matches the Dispose() guard via _disposedValue.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow_WhenClientDisposeAsyncThrows()
    {
        // Symmetric with Dispose_ShouldNotThrowException_WhenRabbitMQClientDisposeThrowsException:
        // disposal exceptions are routed to SelfLog and Trace and swallowed.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.DisposeAsync())
            .Do(_ => throw new Exception("async-boom"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        await Should.NotThrowAsync(() => sut.DisposeAsync().AsTask());
        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WritesToTraceError_WhenClientDisposeAsyncThrows()
    {
        // Mirror of Dispose_WritesToTraceError_WhenRabbitMQClientDisposeThrowsException.
        // Both Dispose paths must surface disposal failures via Trace so callers without
        // a SelfLog listener still see the diagnostic.
        using var listener = new StringBuilderTraceListener();
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            var textFormatter = Substitute.For<ITextFormatter>();
            var messageEvents = Substitute.For<ISendMessageEvents>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.DisposeAsync())
                .Do(_ => throw new Exception("async-trace-boom"));

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

            await sut.DisposeAsync();

            listener.Output.ShouldContain("async-trace-boom");
            listener.Output.ShouldContain("RabbitMQClient");
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_DoesNotInvokeClient()
    {
        // Cross-path idempotency: Dispose() flips _disposedValue, so a follow-up
        // DisposeAsync() must short-circuit. Pins the contract that the two methods
        // share the same disposal state and either entry point closes the door.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // try/finally guarantees the second disposal path runs even if the first throws,
        // so the sink is always disposed (satisfies cs/dispose-not-called-on-throw).
        try
        {
            sut.Dispose();
        }
        finally
        {
            await sut.DisposeAsync();
        }

        await rabbitMQClient.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Dispose_AfterDisposeAsync_DoesNotInvokeClient()
    {
        // Reverse of DisposeAsync_AfterDispose_DoesNotInvokeClient. Whichever path
        // closes the door first, the second path must observe _disposedValue and exit
        // without re-running the underlying client disposal.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // try/finally guarantees the second disposal path runs even if the first throws,
        // so the sink is always disposed (satisfies cs/dispose-not-called-on-throw).
        try
        {
            await sut.DisposeAsync();
        }
        finally
        {
            sut.Dispose();
        }

        await rabbitMQClient.Received(1).DisposeAsync();
    }
#endif

    [Fact]
    public void Emit_Audit_SwallowsListenerException_AndStillRethrowsOriginal()
    {
        // A throwing listener must be swallowed (SelfLog entry) without recursing; the
        // original publish exception still propagates.
        var listener = Substitute.For<ILoggingFailureListener>();
        listener.When(x => x.OnLoggingFailed(
                Arg.Any<object>(),
                Arg.Any<LoggingFailureKind>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<LogEvent>>(),
                Arg.Any<Exception>()))
            .Do(_ => throw new InvalidOperationException("listener-fail"));

        var sut = CreateSut(ClientThatFailsPublish());
        sut.SetFailureListener(listener);

        // Original publish-fail propagates, NOT the listener-fail.
        var thrown = Should.Throw<InvalidOperationException>(() => sut.Emit(LogEventBuilder.Create().Build()));
        thrown.Message.ShouldBe("publish-fail");
    }
}
