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

using System.Diagnostics.CodeAnalysis;
using RabbitMQ.Client;

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
    /// Client provided name to be used for connection.
    /// This option may be used to differentiate between connected applications in RabbitMQ Management UI.
    /// </summary>
    public string? ClientProvidedName { get; set; }

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
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>
    /// When set to <see langword="true"/>, auto create exchange.
    /// </summary>
    public bool AutoCreateExchange { get; set; }

    /// <summary>
    /// Number of channels held in the pool. Channels are opened eagerly at startup.
    /// When all channels are in use, additional publish calls wait until one is returned.
    /// Default is 64.
    /// </summary>
    public int ChannelCount { get; set; } = RabbitMQClient.DEFAULT_CHANNEL_COUNT;

    /// <summary>
    /// Deprecated alias for <see cref="ChannelCount"/>. The pool size has always been a fixed
    /// count rather than a maximum; the property has been renamed to reflect that.
    /// </summary>
    [Obsolete("Use ChannelCount instead. MaxChannels will be removed in a future major version.")]
    public int MaxChannels
    {
        get => ChannelCount;
        set => ChannelCount = value;
    }

    /// <summary>
    /// Maximum number of consecutive warm-up failures before the channel pool enters a
    /// broken state and fails fast from <see cref="IRabbitMQChannelPool.GetAsync"/>.
    /// A half-open circuit breaker attempts recovery after a cooldown window.
    /// Default is 10. Set to 0 to retry indefinitely (preserves pre-9.0 behaviour).
    /// The failure counter resets on any successful channel creation.
    /// </summary>
    public int WarmUpMaxRetries { get; set; } = 10;

    /// <summary>
    /// Contains events for sending messages.
    /// </summary>
    public ISendMessageEvents? SendMessageEvents { get; set; }

    /// <summary>
    /// Create a deep clone.
    /// </summary>
    /// <returns>The created options.</returns>
    public RabbitMQClientConfiguration Clone() =>
        new()
        {
            AutoCreateExchange = AutoCreateExchange,
            ClientProvidedName = ClientProvidedName,
            DeliveryMode = DeliveryMode,
            Exchange = Exchange,
            ExchangeType = ExchangeType,
            Heartbeat = Heartbeat,
            Hostnames = Hostnames.ToList(),
            ChannelCount = ChannelCount,
            WarmUpMaxRetries = WarmUpMaxRetries,
            Password = Password,
            Port = Port,
            RoutingKey = RoutingKey,
            SendMessageEvents = SendMessageEvents,
            SslOption = SslOption,
            Username = Username,
            VHost = VHost,
        };

    /// <summary>
    /// Validate this configuration. Throws if any required value is missing or out of range.
    /// Idempotent and safe to call multiple times.
    /// </summary>
    /// <remarks>
    /// The <see cref="ArgumentException"/> overloads thrown for <see cref="Hostnames"/>,
    /// <see cref="Username"/>, and <see cref="Password"/> deliberately do not set
    /// <see cref="ArgumentException.ParamName"/> — this preserves the exception shape callers
    /// previously observed when the checks lived in <c>LoggerConfigurationRabbitMQExtensions</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <see cref="Hostnames"/> is null or empty, <see cref="Username"/> is null or empty, or <see cref="Password"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="Port"/> is outside the valid TCP range, <see cref="ChannelCount"/> is not greater than zero, or <see cref="WarmUpMaxRetries"/> is negative.</exception>
    [SuppressMessage(
        "Major Code Smell",
        "S3928:Parameter names used into ArgumentException constructors should match an existing one",
        Justification = "Validating an instance property: paramName refers to the property, not a method parameter. Preserves the exception shape callers saw when this validation lived in LoggerConfigurationRabbitMQExtensions.")]
    public void Validate()
    {
        if (Hostnames is null || Hostnames.Count == 0)
        {
            throw new ArgumentException("hostnames cannot be empty, specify at least one hostname");
        }

        if (string.IsNullOrEmpty(Username))
        {
            throw new ArgumentException("username cannot be 'null' or and empty string.");
        }

        if (Password == null)
        {
            throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
        }

        if (Port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "port must be in a valid range (1 and 65535)");
        }

        if (ChannelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ChannelCount), "ChannelCount must be greater than zero.");
        }

        if (WarmUpMaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmUpMaxRetries), "WarmUpMaxRetries must be zero (unlimited) or positive.");
        }
    }
}
