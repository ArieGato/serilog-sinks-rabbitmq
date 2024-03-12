// Copyright 2015-2022 Serilog Contributors
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

using RabbitMQ.Client;
using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Initialization options for <see cref="RabbitMQClient"/>.
/// </summary>
public class RabbitMQClientConfiguration
{
    /// <summary>
    /// List of hosts to use for the connection.
    /// </summary>
    public IList<string> Hostnames { get; set; } = [];

    /// <summary>
    /// Username to use when authenticating to the server.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password to use when authenticating to the server.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The port to connect on.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Virtual host to access during connection.
    /// </summary>
    public string VHost { get; set; } = string.Empty;

    /// <summary>
    /// Heartbeat timeout in milliseconds to use when negotiating with the server.
    /// </summary>
    public ushort Heartbeat { get; set; }

    /// <summary>
    /// TLS options for the connection.
    /// </summary>
    public SslOption? SslOption { get; set; }

    /// <summary>
    /// The exchange name.
    /// </summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// The exchange type.
    /// </summary>
    public string ExchangeType { get; set; } = string.Empty;

    /// <summary>
    /// The exchange delivery mode.
    /// </summary>
    public RabbitMQDeliveryMode DeliveryMode { get; set; } = RabbitMQDeliveryMode.NonDurable;

    /// <summary>
    /// The route key.
    /// </summary>
    public string RouteKey { get; set; } = string.Empty;

    /// <summary>
    /// The route key function that allows to organize dynamic routing.
    /// By default <see langword="null"/> and <see cref="RouteKey"/> option is used instead.
    /// When set this option is used instead of fixed <see cref="RouteKey"/>.
    /// </summary>
    public Func<LogEvent, string>? RouteKeyFunction { get; set; }

    /// <summary>
    /// When set to <see langword="true"/>, auto create exchange.
    /// </summary>
    public bool AutoCreateExchange { get; set; }

    /// <summary>
    /// Maximum number of channels in the channel pool.
    /// Default is 64.
    /// </summary>
    public int MaxChannels { get; set; } = RabbitMQClient.DEFAULT_MAX_CHANNEL_COUNT;

    /// <summary>
    /// Create options from other one.
    /// </summary>
    /// <param name="config">The source options.</param>
    /// <returns>The created options.</returns>
    public RabbitMQClientConfiguration From(RabbitMQClientConfiguration config)
    {
        Username = config.Username;
        Password = config.Password;
        Exchange = config.Exchange;
        ExchangeType = config.ExchangeType;
        DeliveryMode = config.DeliveryMode;
        RouteKey = config.RouteKey;
        RouteKeyFunction = config.RouteKeyFunction;
        Port = config.Port;
        VHost = config.VHost;
        Heartbeat = config.Heartbeat;
        SslOption = config.SslOption;
        AutoCreateExchange = config.AutoCreateExchange;
        MaxChannels = config.MaxChannels;
        Hostnames = config.Hostnames.ToList();

        return this;
    }
}
