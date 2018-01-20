namespace Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ
{
    public enum EncodingMode : byte
    {
        UTF8 = 0,
        Unicode = 1,
        ASCII = 2,
        UTF32 = 3,
        UTF7 = 4
    }
}