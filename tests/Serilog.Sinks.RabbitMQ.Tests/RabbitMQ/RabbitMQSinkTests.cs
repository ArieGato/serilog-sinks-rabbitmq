using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

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
    public async Task OnEmptyBatchAsync_ShouldReturnTask()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        await sut.OnEmptyBatchAsync();

        // should not throw exception
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
    public void Dispose_ShouldNotDeadlock_WhenCalledOnSingleThreadedSynchronizationContext()
    {
        // AsyncHelpers.RunSync must fully isolate async continuations from the caller's
        // SynchronizationContext, otherwise Dispose() will deadlock under a single-threaded
        // UI-style context (WinForms/WPF). We install an outer context whose Post throws —
        // any continuation routed through it is a bug in the sync-over-async bridge and
        // the dispose thread would either fail to complete or hang.
        //
        // The primary contract — no deadlock — is proven by thread.Join(5s) below. An
        // earlier version of this test also asserted that SelfLog stayed empty, but
        // SelfLog is a global static and tests in parallel classes (e.g. the channel-
        // pool warmup-retry tests) write to it; that assertion was inherently flaky. See
        // issue #283 for the broader SelfLog parallel-class hygiene work.
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
    public async Task EmitBatchAsync_ShouldWriteExceptionToSelfLog_WhenPublishThrowsException()
    {
        // Arrange
        var selfLogStringBuilder = new StringBuilder();
        var writer = new StringWriter(selfLogStringBuilder);
        SelfLog.Enable(writer);

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
        await sut.EmitBatchAsync([logEvent1, logEvent2]);

        // Assert
        selfLogStringBuilder.Length.ShouldBeGreaterThan(0);
        failureSink.Received(0).Emit(Arg.Any<LogEvent>());
    }

    [Fact]
    public async Task EmitBatchAsync_ShouldWriteExceptionsToSelfLog_WhenFailureSinkThrowsException()
    {
        // Arrange
        var selfLogStringBuilder = new StringBuilder();
        var writer = new StringWriter(selfLogStringBuilder);
        SelfLog.Enable(writer);

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
    public async Task EmitBatchAsync_ShouldNotThrowException_WhenPublishThrowsException()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>()))
            .Do(_ => throw new Exception("some-message"));

        var failureSink = Substitute.For<ILogEventSink>();
        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, EmitEventFailureHandling.Ignore, failureSink);

        // Act
        var logEvent1 = LogEventBuilder.Create().Build();
        var act = () => sut.EmitBatchAsync([logEvent1]);

        // Assert
        await Should.NotThrowAsync(act);
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

    private static RabbitMQSink CreateSut(
        IRabbitMQClient rabbitMQClient,
        EmitEventFailureHandling emitEventFailureHandling = EmitEventFailureHandling.Ignore,
        ILogEventSink? failureSink = null)
    {
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        return new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents, emitEventFailureHandling, failureSink);
    }

    private static IRabbitMQClient ClientThatFailsPublish(string message = "publish-fail")
    {
        var client = Substitute.For<IRabbitMQClient>();
        client.When(x => x.PublishAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<BasicProperties>(), Arg.Any<string?>()))
            .Do(_ => throw new InvalidOperationException(message));
        return client;
    }

    [Fact]
    public async Task EmitBatchAsync_NotifiesListener_WithPermanent_WhenPublishFailsAndNoFailureSink()
    {
        // Matrix row: only listener configured, publish fails → Permanent + original exception.
        var listener = Substitute.For<ILoggingFailureListener>();
        var sut = CreateSut(ClientThatFailsPublish());
        sut.SetFailureListener(listener);

        await sut.EmitBatchAsync([LogEventBuilder.Create().Build()]);

        listener.Received(1).OnLoggingFailed(
            sut,
            LoggingFailureKind.Permanent,
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<LogEvent>>(),
            Arg.Is<Exception>(e => e is InvalidOperationException && e.Message == "publish-fail"));
    }

    [Fact]
    public async Task EmitBatchAsync_NotifiesListenerAndLegacySink_WhenBothConfiguredAndLegacySucceeds()
    {
        // Matrix row: both wired, legacy succeeds, listener still fires with Permanent +
        // the ORIGINAL publish exception (not an AggregateException).
        var listener = Substitute.For<ILoggingFailureListener>();
        var failureSink = Substitute.For<ILogEventSink>();
        var sut = CreateSut(
            ClientThatFailsPublish(),
            EmitEventFailureHandling.WriteToFailureSink,
            failureSink);
        sut.SetFailureListener(listener);

        var logEvent = LogEventBuilder.Create().Build();
        await sut.EmitBatchAsync([logEvent]);

        failureSink.Received(1).Emit(logEvent);
        listener.Received(1).OnLoggingFailed(
            sut,
            LoggingFailureKind.Permanent,
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<LogEvent>>(),
            Arg.Is<Exception>(e => e is InvalidOperationException));
    }

    [Fact]
    public async Task EmitBatchAsync_EscalatesToFinal_WhenLegacyFailureSinkAlsoThrows()
    {
        // Matrix row: both wired, legacy fails too → Final + AggregateException(publish, sink).
        var listener = Substitute.For<ILoggingFailureListener>();
        var failureSink = Substitute.For<ILogEventSink>();
        failureSink.When(x => x.Emit(Arg.Any<LogEvent>()))
            .Do(_ => throw new InvalidOperationException("sink-fail"));

        var sut = CreateSut(
            ClientThatFailsPublish(),
            EmitEventFailureHandling.WriteToFailureSink,
            failureSink);
        sut.SetFailureListener(listener);

        await sut.EmitBatchAsync([LogEventBuilder.Create().Build()]);

        // Delegate predicate (not expression-tree) because Arg.Is overload selection
        // otherwise picks the Expression<...> form which rejects declaration patterns.
        listener.Received(1).OnLoggingFailed(
            sut,
            LoggingFailureKind.Final,
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<LogEvent>>(),
            Arg.Is<Exception>(e => IsPublishAndSinkAggregate(e)));
    }

    private static bool IsPublishAndSinkAggregate(Exception e) =>
        e is AggregateException agg
        && agg.InnerExceptions.Count == 2
        && agg.InnerExceptions[0].Message == "publish-fail"
        && agg.InnerExceptions[1].Message == "sink-fail";

    [Fact]
    public async Task EmitBatchAsync_DoesNotThrow_WhenFailureListenerThrows()
    {
        // A throwing listener must be swallowed (SelfLog entry) without recursing.
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

        var act = () => sut.EmitBatchAsync([LogEventBuilder.Create().Build()]);

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task EmitBatchAsync_DoesNotNotifyListener_WhenListenerIsNotSet()
    {
        // Baseline: no listener — legacy behaviour unchanged, no exception about null deref.
        var sut = CreateSut(ClientThatFailsPublish());

        var act = () => sut.EmitBatchAsync([LogEventBuilder.Create().Build()]);

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public void Emit_Audit_NotifiesListenerAndRethrows_WhenPublishFails()
    {
        // Audit path: listener is notified before the exception propagates (preserving
        // audit semantics of "throw on failure").
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
    public void SetFailureListener_ReplacesPreviouslyRegisteredListener()
    {
        // Defensive: sinks should be okay with the pipeline calling SetFailureListener
        // more than once during construction, even though the contract says "once".
        var first = Substitute.For<ILoggingFailureListener>();
        var second = Substitute.For<ILoggingFailureListener>();
        var sut = CreateSut(ClientThatFailsPublish());

        sut.SetFailureListener(first);
        sut.SetFailureListener(second);

        Should.Throw<InvalidOperationException>(() => sut.Emit(LogEventBuilder.Create().Build()));

        first.DidNotReceive().OnLoggingFailed(
            Arg.Any<object>(),
            Arg.Any<LoggingFailureKind>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<LogEvent>>(),
            Arg.Any<Exception>());
        second.Received(1).OnLoggingFailed(
            Arg.Any<object>(),
            Arg.Any<LoggingFailureKind>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<LogEvent>>(),
            Arg.Any<Exception>());
    }
}
