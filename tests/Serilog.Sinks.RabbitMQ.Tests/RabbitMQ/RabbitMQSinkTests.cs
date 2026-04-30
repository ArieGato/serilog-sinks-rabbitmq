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
    public async Task EmitBatchAsync_ShouldOnlyForwardUnpublishedTailToFailureSink_WhenPublishFailsMidBatch()
    {
        // Partial-batch correctness: events that already published to the broker must
        // NOT be re-emitted to the failure sink, otherwise downstream systems without
        // MessageId-based idempotency would see duplicates (issue: P1 #6 architect
        // review). Only the failing event and the events after it are un-published
        // from the broker's perspective.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        // Succeed twice, then throw on the third PublishAsync.
        var publishCalls = 0;
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call >= 3)
                {
                    throw new InvalidOperationException("broker-failure");
                }

                return Task.CompletedTask;
            });

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToFailureSink, failureSink);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        // Act
        await sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);

        // Assert — events 1 and 2 published successfully and must NOT be forwarded.
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent1));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent2));

        // Events 3, 4, 5 were never published from the broker's perspective and
        // must all be forwarded for the failure sink to surface durably.
        failureSink.Received(1).Emit(Arg.Is(logEvent3));
        failureSink.Received(1).Emit(Arg.Is(logEvent4));
        failureSink.Received(1).Emit(Arg.Is(logEvent5));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldForwardOnlyLastEvent_WhenOnlyLastPublishFails()
    {
        // Boundary case for the partial-batch slicing: the failing event is the
        // very last one, so the un-published tail contains exactly one element.
        // Proves the slice is `[failingIndex..]` and not `[failingIndex+1..]`
        // (off-by-one in the other direction would silently drop the failing event).
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        var publishCalls = 0;
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call == 5)
                {
                    throw new InvalidOperationException("broker-failure-on-last");
                }

                return Task.CompletedTask;
            });

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToFailureSink, failureSink);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        // Act
        await sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);

        // Assert — only the last (failing) event is forwarded.
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent1));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent2));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent3));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent4));
        failureSink.Received(1).Emit(Arg.Is(logEvent5));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldForwardTailAndRethrow_WhenWriteToFailureSinkAndThrowExceptionAreSet()
    {
        // The "route to failure sink AND still throw" combination
        // (WriteToFailureSink | ThrowException) must observe BOTH semantics on the
        // same partial-batch slice: the un-published tail goes to the failure sink
        // AND the original exception propagates so a Fallback wrapper / BatchingSink
        // listener also sees the failure.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        var publishCalls = 0;
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call >= 3)
                {
                    throw new InvalidOperationException("broker-failure");
                }

                return Task.CompletedTask;
            });

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(
            rabbitMQClient,
            textFormatter,
            messageEvents,
            EmitEventFailureHandling.WriteToFailureSink | EmitEventFailureHandling.ThrowException,
            failureSink);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        // Act
        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);

        // Assert — exception still propagates for downstream listeners.
        var thrown = await Should.ThrowAsync<InvalidOperationException>(act);
        thrown.Message.ShouldBe("broker-failure");

        // Slicing applies independently of rethrow: only events 3-5 forwarded.
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent1));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent2));
        failureSink.Received(1).Emit(Arg.Is(logEvent3));
        failureSink.Received(1).Emit(Arg.Is(logEvent4));
        failureSink.Received(1).Emit(Arg.Is(logEvent5));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldWriteSelfLogAndForwardTail_WhenWriteToSelfLogAndWriteToFailureSinkAreSet()
    {
        // WriteToSelfLog | WriteToFailureSink — the diagnostic flag on top of the
        // failure-sink routing. Confirms both the SelfLog write and the partial-tail
        // slicing coexist; without the ThrowException flag the legacy catch-and-route
        // path applies and no exception escapes.
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();

        var publishCalls = 0;
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>())
            .Returns(_ =>
            {
                int call = Interlocked.Increment(ref publishCalls);
                if (call >= 3)
                {
                    throw new InvalidOperationException("broker-failure");
                }

                return Task.CompletedTask;
            });

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(
            rabbitMQClient,
            textFormatter,
            messageEvents,
            EmitEventFailureHandling.WriteToSelfLog | EmitEventFailureHandling.WriteToFailureSink,
            failureSink);

        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var logEvent3 = LogEventBuilder.Create().Build();
        var logEvent4 = LogEventBuilder.Create().Build();
        var logEvent5 = LogEventBuilder.Create().Build();

        // Act
        await sut.EmitBatchAsync([logEvent1, logEvent2, logEvent3, logEvent4, logEvent5]);

        // Assert — SelfLog received the exception summary.
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        selfLogStringBuilder.ToString().ShouldContain("broker-failure");

        // And only events 3-5 forwarded.
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent1));
        failureSink.DidNotReceive().Emit(Arg.Is(logEvent2));
        failureSink.Received(1).Emit(Arg.Is(logEvent3));
        failureSink.Received(1).Emit(Arg.Is(logEvent4));
        failureSink.Received(1).Emit(Arg.Is(logEvent5));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldWriteAllEventsToFailureSink_WhenPublishThrowsException()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToFailureSink, failureSink);

        // Act
        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        await sut.EmitBatchAsync([logEvent1, logEvent2]);

        // Assert
        failureSink.Received(1).Emit(Arg.Is(logEvent1));
        failureSink.Received(1).Emit(Arg.Is(logEvent2));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldWriteExceptionToSelfLogAndRethrow_WhenPublishThrowsException()
    {
        // Arrange — WriteToSelfLog logs to SelfLog first, then rethrows so BatchingSink
        // (or any Fallback wrapper) can route the failure via its own listener plumbing.
        // Previously the flag also caused silent swallow; that default no longer applies.
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToSelfLog, failureSink);

        // Act
        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2]);

        // Assert
        var thrown = await Should.ThrowAsync<Exception>(act);
        thrown.Message.ShouldBe("some-message");
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        failureSink.Received(0).Emit(Arg.Any<LogEvent>());
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldWriteExceptionsToSelfLog_WhenFailureSinkThrowsException()
    {
        // Arrange
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        failureSink.When(x => x.Emit(Arg.Any<LogEvent>()))
            .Do(_ => throw new Exception("failure-sink-message"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToFailureSink, failureSink);

        // Act
        var logEvent1 = LogEventBuilder.Create().Build();
        await sut.EmitBatchAsync([logEvent1]);

        // Assert
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        selfLogStringBuilder.ToString().ShouldContain("some-message");
        selfLogStringBuilder.ToString().ShouldContain("failure-sink-message");
        failureSink.Received(1).Emit(Arg.Is(logEvent1));
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldThrowException_WhenPublishThrowsException()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.ThrowException, failureSink);

        // Act
        var logEvent1 = LogEventBuilder.Create().Build();
        var logEvent2 = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent1, logEvent2]);

        // Assert
        var ex = await Should.ThrowAsync<Exception>(act);
        ex.Message.ShouldBe("some-message");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldNotThrow_WhenWriteToFailureSinkIsConfigured()
    {
        // After the 9.0 alignment with Serilog's BatchingSink model, WriteToFailureSink is
        // the only flag combination that suppresses the exception. Everything else (default
        // Ignore, WriteToSelfLog alone, ThrowException) rethrows so BatchingSink can route
        // through its listener plumbing.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.WriteToFailureSink, failureSink);

        var logEvent1 = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent1]);

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldRethrow_WhenHandlingIsIgnoreAndPublishFails()
    {
        // Default handling (Ignore) now propagates publish failures so BatchingSink sees
        // them. This is the behaviour change that makes WriteTo.Fallback(...) work for
        // the batched pipeline.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException("publish-fail"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.Ignore);

        var logEvent = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent]);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(act);
        thrown.Message.ShouldBe("publish-fail");
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldLogToSelfLogAndEmitToFailureSinkWithoutRethrowing_WhenWriteToFailureSinkAndWriteToSelfLogAreCombined()
    {
        // Pins the WriteToFailureSink | WriteToSelfLog row of the behaviour matrix: both
        // the SelfLog entry and the per-event emit to the failure sink run, and the
        // exception is swallowed (WriteToFailureSink suppresses the rethrow).
        using var selfLog = new SelfLogScope(out var selfLogStringBuilder);

        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException("publish-fail"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(
            rabbitMQClient,
            textFormatter,
            messageEvents,
            EmitEventFailureHandling.WriteToSelfLog | EmitEventFailureHandling.WriteToFailureSink,
            failureSink);

        var logEvent = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent]);

        await Should.NotThrowAsync(act);
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        failureSink.Received(1).Emit(logEvent);
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldRouteToFailureSinkAndStillRethrow_WhenWriteToFailureSinkIsCombinedWithThrowException()
    {
        // The combination "route events to my failure sink AND still throw" — covers users
        // who want legacy routing AND want BatchingSink's listener to fire too.
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException("publish-fail"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(
            rabbitMQClient,
            textFormatter,
            messageEvents,
            EmitEventFailureHandling.WriteToFailureSink | EmitEventFailureHandling.ThrowException,
            failureSink);

        var logEvent = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent]);

        await Should.ThrowAsync<InvalidOperationException>(act);
        failureSink.Received(1).Emit(logEvent);
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
