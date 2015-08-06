namespace Serilog.Sinks.RabbitMQ
{
    public enum RabbitMQDeliveryMode : byte
    {
        NonDurable = 1,
        Durable = 2
    }
}
