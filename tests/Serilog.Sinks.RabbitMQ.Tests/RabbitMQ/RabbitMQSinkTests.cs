using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ
{
    public class RabbitMQSinkTests
    {
        [Fact]
        public void Emit_ShouldPublishMessages()
        {
            // Arrange
            var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message", []), new List<LogEventProperty>());

            var textFormatter = Substitute.For<ITextFormatter>();
            textFormatter
                .When(x => x.Format(Arg.Any<LogEvent>(), Arg.Any<TextWriter>()))
                .Do(x => x.Arg<TextWriter>().Write(x.Arg<LogEvent>().MessageTemplate.Text));

            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

            // Act
            sut.Emit(logEvent);

            // Assert
            rabbitMQClient.Received(1).Publish(Arg.Is("some-message"));
        }

        [Fact]
        public async Task EmitBatchAsync_ShouldPublishMessages()
        {
            // Arrange
            IEnumerable<LogEvent> logEvents = [
                new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message-1", []), new List<LogEventProperty>()),
                new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, new MessageTemplate("some-message-2", []), new List<LogEventProperty>())];

            var textFormatter = Substitute.For<ITextFormatter>();
            textFormatter
                .When(x => x.Format(Arg.Any<LogEvent>(), Arg.Any<TextWriter>()))
                .Do(x => x.Arg<TextWriter>().Write(x.Arg<LogEvent>().MessageTemplate.Text));

            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

            // Act
            await sut.EmitBatchAsync(logEvents);

            // Assert
            rabbitMQClient.Received(1).Publish(Arg.Is("some-message-1"));
            rabbitMQClient.Received(1).Publish(Arg.Is("some-message-2"));
        }

        [Fact]
        public async Task EmitBatchAsync_ShouldDoNothing_WhenNoEventsAreEmitted()
        {
            // Arrange
            IEnumerable<LogEvent> logEvents = [];

            var textFormatter = Substitute.For<ITextFormatter>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

            // Act
            await sut.EmitBatchAsync(logEvents);

            // Assert
            rabbitMQClient.DidNotReceive().Publish(Arg.Any<string>());
        }

        [Fact]
        public async Task OnEmptyBatchAsync_ShouldReturnTask()
        {
            // Arrange
            var textFormatter = Substitute.For<ITextFormatter>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

            // Act
            await sut.OnEmptyBatchAsync();

            // should not throw exception
        }

        [Fact]
        public void Dispose_ShouldCloseAndDisposeRabbitMQClient()
        {
            // Arrange
            var textFormatter = Substitute.For<ITextFormatter>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

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
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

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
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Close())
                .Do(_ => throw new Exception("some-message"));

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

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
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Publish(Arg.Any<string>()))
                .Do(_ => throw new Exception("some-message"));

            var failureSink = Substitute.For<ILogEventSink>();
            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, EmitEventFailureHandling.WriteToFailureSink, failureSink);

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
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Publish(Arg.Any<string>()))
                .Do(_ => throw new Exception("some-message"));

            var failureSink = Substitute.For<ILogEventSink>();
            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, EmitEventFailureHandling.WriteToSelfLog, failureSink);

            // Act
            var logEvent1 = LogEventBuilder.Create().Build();
            var logEvent2 = LogEventBuilder.Create().Build();
            await sut.EmitBatchAsync([logEvent1, logEvent2]);

            // Assert
            selfLogStringBuilder.Length.Should().BeGreaterThan(0);
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
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Publish(Arg.Any<string>()))
                .Do(_ => throw new Exception("some-message"));

            var failureSink = Substitute.For<ILogEventSink>();
            failureSink.When(x => x.Emit(Arg.Any<LogEvent>()))
                .Do(_ => throw new Exception("failure-sink-message"));

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, EmitEventFailureHandling.WriteToFailureSink, failureSink);

            // Act
            var logEvent1 = LogEventBuilder.Create().Build();
            await sut.EmitBatchAsync([logEvent1]);

            // Assert
            selfLogStringBuilder.Length.Should().BeGreaterThan(0);
            selfLogStringBuilder.ToString().Should().Contain("some-message");
            selfLogStringBuilder.ToString().Should().Contain("failure-sink-message");
            failureSink.Received(1).Emit(Arg.Is(logEvent1));
        }

        [Fact]
        public async Task EmitBatchAsync_ShouldThrowException_WhenPublishThrowsException()
        {
            // Arrange
            var textFormatter = Substitute.For<ITextFormatter>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Publish(Arg.Any<string>()))
                .Do(_ => throw new Exception("some-message"));

            var failureSink = Substitute.For<ILogEventSink>();
            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, EmitEventFailureHandling.ThrowException, failureSink);

            // Act
            var logEvent1 = LogEventBuilder.Create().Build();
            var logEvent2 = LogEventBuilder.Create().Build();
            var act = () => sut.EmitBatchAsync([logEvent1, logEvent2]);

            // Assert
            await act.Should().ThrowAsync<Exception>().WithMessage("some-message");
        }

        [Fact]
        public async Task EmitBatchAsync_ShouldNotThrowException_WhenPublishThrowsException()
        {
            // Arrange
            var textFormatter = Substitute.For<ITextFormatter>();
            var rabbitMQClient = Substitute.For<IRabbitMQClient>();
            rabbitMQClient.When(x => x.Publish(Arg.Any<string>()))
                .Do(_ => throw new Exception("some-message"));

            var failureSink = Substitute.For<ILogEventSink>();
            var sut = new RabbitMQSink(rabbitMQClient, textFormatter, EmitEventFailureHandling.Ignore, failureSink);

            // Act
            var logEvent1 = LogEventBuilder.Create().Build();
            var act = () => sut.EmitBatchAsync([logEvent1]);

            // Assert
            await act.Should().NotThrowAsync<Exception>();
        }
    }
}
