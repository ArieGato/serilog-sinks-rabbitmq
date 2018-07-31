// Copyright 2015 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Security.Authentication;
using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ
{
    /// <summary>
    /// Configuration class for RabbitMqClient
    /// </summary>
    public class RabbitMQConfiguration
    {
        public string Hostname = string.Empty;
        public string Username = string.Empty;
        public string Password = string.Empty;
        public string Exchange = string.Empty;
        public string ExchangeType = string.Empty;
        public RabbitMQDeliveryMode DeliveryMode = RabbitMQDeliveryMode.NonDurable;
        public string RouteKey = string.Empty;
        public int Port;
        public string VHost = string.Empty;
        public IProtocol Protocol;
        public ushort Heartbeat;
        public int BatchPostingLimit;
        public TimeSpan Period;
        public SslProtocols? SslProtocols;
        public bool? SslEnabled;
    }
}
