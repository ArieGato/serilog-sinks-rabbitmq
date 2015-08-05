using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ
{
    /// <summary>
    /// Configuration class for RabbitMqClient
    /// </summary>
    public class RabbitMQConfiguration
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
