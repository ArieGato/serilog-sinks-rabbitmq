using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ
{
    public class RabbitMQSinkTests
    {
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
            await rabbitMQClient.Received(1).PublishAsync(Arg.Is("some-message-1"));
            await rabbitMQClient.Received(1).PublishAsync(Arg.Is("some-message-2"));
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
            await rabbitMQClient.DidNotReceive().PublishAsync(Arg.Any<string>());
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
                .Do(x => throw new Exception("some-message"));

            var sut = new RabbitMQSink(rabbitMQClient, textFormatter);

            // Act
            sut.Dispose();

            // Assert
            rabbitMQClient.Received(1).Close();
            rabbitMQClient.Received(1).Dispose();
        }
    }
}
