# serilog-sinks-rabbitmq

![License](https://img.shields.io/github/license/ArieGato/serilog-sinks-rabbitmq)

[![codecov](https://codecov.io/gh/ArieGato/serilog-sinks-rabbitmq/graph/badge.svg?token=4uZ4oaOQUE)](https://codecov.io/gh/ArieGato/serilog-sinks-rabbitmq)
[![Nuget](https://img.shields.io/nuget/dt/Serilog.Sinks.RabbitMQ)](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ)
[![Nuget](https://img.shields.io/nuget/v/Serilog.Sinks.RabbitMQ)](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ)

[![GitHub Release Date](https://img.shields.io/github/release-date/ArieGato/serilog-sinks-rabbitmq?label=released)](https://github.com/ArieGato/serilog-sinks-rabbitmq/releases)
[![GitHub commits since latest release (by date)](https://img.shields.io/github/commits-since/ArieGato/serilog-sinks-rabbitmq/latest?label=new+commits)](https://github.com/ArieGato/serilog-sinks-rabbitmq/commits/master)
![Size](https://img.shields.io/github/repo-size/ArieGato/serilog-sinks-rabbitmq)

[![GitHub contributors](https://img.shields.io/github/contributors/ArieGato/serilog-sinks-rabbitmq)](https://github.com/ArieGato/serilog-sinks-rabbitmq/graphs/contributors)
![Activity](https://img.shields.io/github/commit-activity/w/ArieGato/serilog-sinks-rabbitmq)
![Activity](https://img.shields.io/github/commit-activity/m/ArieGato/serilog-sinks-rabbitmq)
![Activity](https://img.shields.io/github/commit-activity/y/ArieGato/serilog-sinks-rabbitmq)

[![Run unit tests](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/tests.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/tests.yml)
[![Publish preview to GitHub registry](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-prerelease.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-prerelease.yml)
[![Publish release to Nuget registry](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-release.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/publish-release.yml)
[![CodeQL analysis](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/ArieGato/serilog-sinks-rabbitmq/actions/workflows/codeql-analysis.yml)

## Purpose

This project is to allow Serilog to log to RabbitMQ using the [RabbitMQ.Client](https://www.nuget.org/packages/RabbitMQ.Client) package.
The aim is to expose RabbitMQ.Client functionality, in a logical way, and not to build in additional logic into the sink. So expect pure
RabbitMQ.Client behavior, but perhaps a little bit simpler interface.

## Versioning

As of v3.0.0 we use [Semantic Versioning](https://semver.org) to express changes in the API. 

### Dependencies

|Serilog.Sinks.RabbitMQ|.NETStandard|.NETFramework|Serilog|RabbitMQ.Client|
|---|---|---|---|---|
|2.0.0|1.6.0|4.5.1|2.3.0|4.\*|
|3.0.0|1.6.1|4.5.1|2.8.0|5.\*|
|6.0.0|2.0.0|4.7.2|2.8.0|6.\*|
|7.0.0|2.0.0|-|3.1.1|6.8.\*|

## Installation

Using [Nuget](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ/):

```
Install-Package Serilog.Sinks.RabbitMQ
```

## Release Notes

See [changelog](CHANGELOG.md).

## Topics

* [Sink Configuration Options](#sink-configuration-options)
* [External configuration using Serilog.Settings.AppSettings](#external-configuration-using-serilogsettingsappsettings)
* [External configuration using Serilog.Settings.Configuration](#external-configuration-using-serilogsettingsconfiguration)
* [Audit Sink Configuration](#audit-sink-configuration)
* [Multihost configuration](#multihost-configuration)
* [Configuration via code](#configuration-via-code)

### Sink Configuration Options

The sink can be configured completely through code, by using configuration files (or other types of configuration providers), 
a combination of both, or by using the various Serilog configuration packages. 
The sink is configured with a typical Serilog `WriteTo` configuration method (or `AuditTo`, or similar variations).

All sink configuration methods accept the following arguments, though not necessarily in this order. 
Use of named arguments is strongly recommended.

* `hostnames`
* `username`
* `password`
* `exchange`
* `exchangeType`
* `deliveryMode`
* `routingKey`
* `port`
* `vHost`
* `heartbeat`
* `sslEnabled`
* `sslServerName`
* `sslVersion`
* `sslAcceptablePolicyErrors`
* `sslCheckCertificateRevocation`
* `batchPostingLimit`
* `bufferingTimeLimit`
* `queueLimit`
* `formatter`
* `autoCreateExchange`
* `maxChannels`
* `levelSwitch`

### Arguments

Parameters `exchange`, `exchangeType`, `deliveryMode`, `routingKey` provide additional configuration when connecting to RabbitMQ.
If `autoCreateExchange` is `true`, the sink will create the exchange if an exchange by that name doesn't exist.
Exchange is not created by default.

If `sslEnabled` is `true`, the sink will use secure connection to server.
By default, the server name is the same as the host name, no TLS version is specified, no server certificate errors are accepted,
and certificate revocation checking is disabled. You can change server name through by setting the `sslServerName`, `sslVersion`,
`sslAcceptablePolicyErrors`, `sslCheckCertificateRevocation` arguments.

This is a "periodic batching sink." The sink will queue a certain number of log events before they're actually written to RabbitMQ. 
There is also a timeout period so that the batch is always written even if it has not been filled. 
By default, the batch size is 50 rows and the timeout is 2 seconds. 
You can change these through by setting the `batchPostingLimit` and `bufferingTimeLimit` arguments.

Refer to the [Formatter](https://github.com/serilog/serilog/wiki/Formatting-Output#formatting-json) for details about the _formatter_ arguments.

### Code-Only (any .NET target)

All sink features are configurable from code. Here is a typical example that works the same way for any .NET target.

```C#
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ(
        username: "usr",
        password: "pwd",
        hostnames: new[] { "localhost" },
        port: 5672,
        exchange = "LogExchange",
        formatter: new JsonFormatter()
    ).CreateLogger();
```

## External configuration using Serilog.Settings.AppSettings

Refer to the [Serilog.Settings.AppSettings](https://github.com/serilog/serilog-settings-appsettings/blob/dev/README.md) package documentation
for complete details about sink configuration. This is an example of setting some of the configuration parameters for this sink.

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ"/>
<add key="serilog:write-to:RabbitMQ.username" value="user"/>
<add key="serilog:write-to:RabbitMQ.password" value="pwd"/>
<add key="serilog:write-to:RabbitMQ.hostnames" value="server1,server2"/>
<add key="serilog:write-to:RabbitMQ.exchange" value="LogExchange"/>
<add key="serilog:write-to:RabbitMQ.batchPostingLimit" value="1000"/>
<add key="serilog:write-to:RabbitMQ.bufferingTimeLimit" value="0.00:00:02.00"/>
```

## External configuration using Serilog.Settings.Configuration

Refer to the [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration/blob/dev/README.md) package documentation
for complete details about sink configuration. Keys and values are not case-sensitive. This is an example of configuring the sink arguments.

```json
{
  "Serilog": {
    "Using":  ["Serilog.Sinks.RabbitMQ"],
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "RabbitMQ",
        "Args": {
          "username": "usr",
          "password": "pwd",
          "hostnames": [
            "localhost"
          ],
          "port": 5672,
          "exchange": "LogExchange",
          "autoCreateExchange": true,
          "batchPostingLimit": 1000,
          "bufferingTimeLimit": "0.00.00.02.00"
        } 
      }
    ]
  }
}
```

## Audit Sink Configuration

A Serilog audit sink writes log events which are of such importance that they must succeed, and that verification of a successful write is more
important than write performance. Unlike the regular sink, an audit sink _does not_ fail silently - it can throw exceptions. You should wrap
audit logging output in a `try/catch` block. The usual example is bank account withdrawal events - a bank would certainly not want to allow a
failure to record those transactions to fail silently.

The constructor accepts most of the same arguments, and like other Serilog audit sinks, you configure one by using `AuditTo` instead of `WriteTo`.

* `hostnames`
* `username`
* `password`
* `exchange`
* `exchangeType`
* `deliveryMode`
* `routingKey`
* `port`
* `vHost`
* `heartbeat`
* `sslEnabled`
* `sslServerName`
* `sslVersion`
* `sslAcceptablePolicyErrors`
* `sslCheckCertificateRevocation`
* `formatter`
* `autoCreateExchange`
* `maxChannels`
* `levelSwitch`

The _batchPostingLimit_ and _bufferingTimeLimit_ parameters are not available because the audit sink writes log events immediately.

```json
{
  "Serilog": {
    "Using":  ["Serilog.Sinks.RabbitMQ"],
    "MinimumLevel": "Debug",
    "AuditTo": [
      {
        "Name": "RabbitMQ",
        "Args": {
          "username": "usr",
          "password": "pwd",
          "hostnames": [
            "localhost"
          ],
          "port": 5672,
          "exchange": "LogExchange",
          "autoCreateExchange": true
        }
      }
    ]
  }
}
```

## Multihost Configuration

The sink can be configured taking multiple host names.  
To keep the _Serilog.Setting.ApSettings_ external configuration, additional hosts are added to the `hostnames` argument separated by commas.
This is an example of configuring the multihost using _Serilog.Settings.AppSettings_.

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ"/>
<add key="serilog:write-to:RabbitMQ.hostnames" value="host1,host2"/>
<add key="serilog:write-to:RabbitMQ.username" value="user"/>
<add key="serilog:write-to:RabbitMQ.pasword" value="pwd"/>
```

## Use protected configuration (ASP.NET)

ASP.NET has the possibility to encrypt connection string in the web.config. 

## Configuration via code

There are multiple ways for configuring the RabbitMQSink with the release of v3.0.0

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
    {
        clientConfiguration.Username     = _config["RABBITMQ_USER"];
        clientConfiguration.Password     = _config["RABBITMQ_PASSWORD"];
        clientConfiguration.Exchange     = _config["RABBITMQ_EXCHANGE"];
        clientConfiguration.ExchangeType = _config["RABBITMQ_EXCHANGE_TYPE"];
        clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
        clientConfiguration.RoutingKey     = "Logs";
        clientConfiguration.Port         = 5672;

        foreach (string hostname in _config.GetSection("RABBITMQ_HOSTNAMES").Get<string[]>())
            clientConfiguration.Hostnames.Add(hostname);

        sinkConfiguration.TextFormatter  = new JsonFormatter();
    }).CreateLogger();
```

```csharp
// Or
var config = new RabbitMQClientConfiguration
{
    Port            = 5672,
    DeliveryMode    = RabbitMQ.RabbitMQDeliveryMode.Durable,
    Exchange        = "test_exchange",
    Username        = "guest",
    Password        = "guest",
    ExchangeType    = "fanout"
};

foreach (string hostname in _config["RABBITMQ_HOSTNAMES"])
    config .Hostnames.Add(hostname);

Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
    {
        clientConfiguration.From(config);
        sinkConfiguration.TextFormatter = new JsonFormatter();
    }) .CreateLogger();
```

```csharp
// Or
Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
    {
        clientConfiguration.From(Configuration.Bind("RabbitMQClientConfiguration", new RabbitMQClientConfiguration()));
        sinkConfiguration.TextFormatter = new JsonFormatter();
    }).CreateLogger();
```

```csharp
// Or
LoggerConfiguration loggerConfiguration = new LoggerConfiguration();
IConfigurationSection rabbitMqSection = configuration.GetSection("log:rabbitMq");
loggerConfiguration = loggerConfiguration
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
    {
        rabbitMqSection.Bind(clientConfiguration);
        sinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Warning;
    });
```    
   
```csharp
// At last, don't forget to register the logger into the services
var loggerFactory = new LoggerFactory();
loggerFactory
  .AddSerilog() // if you are not assigning the logger to Log.Logger, then you need to add your logger here.
  .AddConsole(LogLevel.Information);

services.AddSingleton<ILoggerFactory>(loggerFactory);
```

## Customize Message Properties and Routing Key

In order to set message properties, you can create a class which implements `ISendMessageEvents`.
This interface describes two methods that you must implement. The first is `OnSetMessageProperties` which is called before
the message is sent to RabbitMQ. The second is `OnGetRoutingKey` which is called to determine the routing key for the message.

```csharp
public class CustomMessageEvents : ISendMessageEvents
{
    /// <inheritdoc />
    public void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties)
    {
        // example of setting message header based on log event level
        properties.Headers = new Dictionary<string, object?>
        {
            { "log-level", logEvent.Level.ToString() },
        };

        // example of setting correlation id based on log event properties
        if (logEvent.Properties.TryGetValue(LogProperties.CORRELATION_ID, out var correlationId))
        {
            properties.CorrelationId = correlationId.ToString();
        }
    }

    /// <inheritdoc />
    public string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey)
    {
        // example of routing based on log level
        return logEvent.Level switch
        {
            LogEventLevel.Error => "error",
            _ => _defaultRoutingKey
        };
    }
}
```

## References

- [Serilog](https://serilog.net/)
- [RabbitMQ Client](https://github.com/rabbitmq/rabbitmq-dotnet-client)
- [Logging in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
- [Dependency Injection in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
