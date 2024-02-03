namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

[CollectionDefinition("Sequential")]
public class RabbitMQCollection : ICollectionFixture<RabbitMQFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}