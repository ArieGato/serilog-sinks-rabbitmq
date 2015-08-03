using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.RabbitMq.Sinks.RabbitMq
{
    public class RabbitMqMessage
    {
        private readonly string _message;

        public RabbitMqMessage(string message)
        {
            _message = message;
        }

        public string Data { get; set; }

        public override string ToString()
        {
            return "{ \"message\" : \"" + _message + "\", \"data\" : \"" + Data + "\" }";
        }
    }
}
