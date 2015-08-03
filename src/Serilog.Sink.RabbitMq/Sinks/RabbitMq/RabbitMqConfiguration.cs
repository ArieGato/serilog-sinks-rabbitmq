using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMq.Sinks.RabbitMq
{
    /// <summary>
    /// Configuration class for RabbitMqClient
    /// </summary>
    public class RabbitMqConfiguration
    {
        public string Hostname = "";
        public string Username = "";
        public string Password = "";
        public string Exchange = "";
        public string Queue = "";
        public string RouteKey = "";
        public int Port;
        public string VHost = "";
        public IProtocol Protocol;
    }
}
