using Microsoft.Extensions.ObjectPool;
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

        public void Close() => throw new NotImplementedException();

        public Task CloseAsync() => throw new NotImplementedException();

        public void Dispose() => throw new NotImplementedException();

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
    public void Dispose_ShouldCloseAndDisposeRabbitMQClient()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Dispose();

        // Assert
        rabbitMQClient.Received(1).Close();
        rabbitMQClient.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_ShouldNotThrowException_WhenCalledTwice()
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
        rabbitMQClient.Received(1).Close();
        rabbitMQClient.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_ShouldNotThrowException_WhenRabbitMQClientCloseThrowsException()
    {
        // Arrange
        var textFormatter = Substitute.For<ITextFormatter>();
        var messageEvents = Substitute.For<ISendMessageEvents>();
        var rabbitMQClient = Substitute.For<IRabbitMQClient>();
        rabbitMQClient.When(x => x.Close())
            .Do(_ => throw new Exception("some-message"));

        var sut = new RabbitMQSink(rabbitMQClient, textFormatter, messageEvents);

        // Act
        sut.Dispose();

        // Assert
        rabbitMQClient.Received(1).Close();
        rabbitMQClient.Received(1).Dispose();
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
        var rabbitMQChannelObjectPoolPolicy = Substitute.For<IPooledObjectPolicy<IRabbitMQChannel>>();
        var rabbitMQChannel = Substitute.For<IRabbitMQChannel>();
        rabbitMQChannelObjectPoolPolicy.Create().Returns(rabbitMQChannel);

        // configure default send message events
        var messageEvents = new SendMessageEvents();

        var rabbitMQClient = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, rabbitMQChannelObjectPoolPolicy);

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
}
