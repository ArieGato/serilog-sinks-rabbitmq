using System;
using RabbitMQ.Client;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq
    /// </summary>
    public class RabbitMQClient
    {
        // configuration members
        public string Hostname;
        public string Username;
        public string Password;
        public string Exchange;
        public string Queue;
        public string RouteKey;
        public int Port;
        public string VHost;
        public IProtocol Protocol;

        // endpoint members
        public IConnectionFactory ConnectionFactory;
        public IConnection Connection;
        public IModel Model;
        public IBasicProperties Properties;
        public PublicationAddress Address;

        /// <summary>
        /// Constructor for RabbitMqClient
        /// </summary>
        /// <param name="configuration">mandatory</param>
        public RabbitMQClient(RabbitMQConfiguration configuration)
        {
            //map members from configuration
            Hostname = configuration.Hostname;
            Username = configuration.Username;
            Password = configuration.Password;
            Exchange = configuration.Exchange;
            Queue = configuration.Queue;
            RouteKey = configuration.RouteKey;
            Port = configuration.Port;
            VHost = configuration.VHost;
            Protocol = configuration.Protocol;

            // prepare publication address
            Address = new PublicationAddress(ExchangeType.Topic, Exchange, RouteKey);

            // initialize 
            InitializeEndpoint();
        }

        /// <summary>
        /// Private method, that must be run for the client to work.
        /// <remarks>See constructor</remarks>
        /// </summary>
        private void InitializeEndpoint()
        {
            // prepare endpoint
            ConnectionFactory = GetConnectionFactory();
            Connection = ConnectionFactory.CreateConnection();
            Model = Connection.CreateModel();
            Properties = Model.CreateBasicProperties();
        }

        /// <summary>
        /// Configures a new ConnectionFactory, and returns it
        /// </summary>
        /// <returns></returns>
        private IConnectionFactory GetConnectionFactory()
        {
            // prepare connection factory
            var connectionFactory = new ConnectionFactory();
            connectionFactory.HostName = Hostname;
            connectionFactory.UserName = Username;
            connectionFactory.Password = Password;

            // only set, if has value, otherwise leave default
            if (Port > 0) connectionFactory.Port = Port;
            if (!string.IsNullOrEmpty(VHost)) connectionFactory.VirtualHost = VHost;
            if (Protocol != null) connectionFactory.Protocol = Protocol;

            // return factory
            return connectionFactory;
        }

        /// <summary>
        /// Publishes a message to RabbitMq Exchange
        /// </summary>
        /// <param name="message"></param>
        public void Publish(string message)
        {
            // push message to queue
            Model.BasicPublish(Address, Properties, System.Text.Encoding.UTF8.GetBytes(message));
        }
    }
}