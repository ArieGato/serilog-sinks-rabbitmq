# serilog-sinks-rabbitmq
![Build Status](https://ci.appveyor.com/api/projects/status/row7ob6fhgmvtqwb?svg=true)
<a href="https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ/">
  <img src="https://img.shields.io/nuget/v/Serilog.Sinks.RabbitMQ.svg" />
</a>
[![Gitter](https://badges.gitter.im/serilog-sinks-rabbitmq/serilog-sinks-rabbitmq.svg)](https://gitter.im/serilog-sinks-rabbitmq/Lobby)

## Purpose

This project is to allow Serilog to log to RabbitMQ using the [RabbitMQ.Client](https://www.nuget.org/packages/RabbitMQ.Client) package. The aim is to expose RabbitMQ.Client functionality, in a logical way, and not to build in additional logic into the sink. So expect pure RabbitMQ.Client behavior, but perhaps a little bit simpler interface.

## Versioning

As of v3.0.0 we use [Semantic Versioning](https://semver.org) to express changes in the API. 

### Dependencies

|Serilog.Sinks.RabbitMQ|.NETStandard|.NETFramework|Serilog|RabbitMQ.Client|
|---|---|---|---|---|
|2.0.0|1.6.0|4.5.1|2.3.0|4.\*|
|3.0.0|1.6.1|4.5.1|2.8.0|5.1.0|

## Installation

Using [Nuget](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ/):

```
Install-Package Serilog.Sinks.RabbitMQ
```

## Version 2.0.0 configuration

#### Topics

* [Sink Configuration Options](#sink-configuration-options)
* [Audit Sink Configuration](#audit-sink-configuration)
* [Multihost configuration](#multihost-configuration)

### Sink Configuration Options

The sink can be configured completely through code, by using configuration files (or other types of configuration providers), 
a combination of both, or by using the various Serilog configuration packages. 
The sink is configured with a typical Serilog `WriteTo` configuration method (or `AuditTo`, or similar variations).

All sink configuration methods accept the follwing arguments, though not necessarily in this order. 
Use of named arguments is strongly recommended.

* `amqpUri`
* `hostanme`
* `username`
* `password`
* `port`
* `vHost`
* `exchange`
* `exchangeType`
* `deliveryMode`
* `routeKey`
* `batchPostingLimit`
* `period`
* `formatter`

### Arguments

At minimum, `amqpUri`.  Refer to [RabbitMQ URI Specification](https://www.rabbitmq.com/uri-spec.html) for details about the _amqpUri_ arguments.
If you are using an external configuration source such as an XML file, you can use a named reference to connection string instead of providing the full "raw" uri. 
This is necessary in cases where in ASP.NET applications need to encrypt the connection settings for RabbitMQ.

Parameters `exchange`, `exchangeType`, `deliveryMode`, `routeKey` provide additional configuration when connecting to RabbitMQ.

If `autoCreateExchange` is `true`, the sink will create the exchange if a exchange by that name doesn't exist.

This is a "periodic batching sink." The sink will queue a certain number of log events before they're actually written to RabbitMQ. 
There is also a timeout period so that the batch is always written even if it has not been filled. By default, the batch size is 50 rows and the timeout is 2 seconds. 
You can change these through by setting the `batchPostingLimit` and `period` arguments.

Refer to the [Formatter](https://github.com/serilog/serilog/wiki/Formatting-Output#formatting-json) for details about the _formatter_ arguments.

For backward compatibility can use parameters `hostname`, `username`, `password`, `port` and `vHost`. 

### Code-Only (any .NET target)

All sink features are configurable from code. Here is a typical example that works the same way for any .NET target.

```C#
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ(
        amqpUri: "amqp://user:pwd@host:5672/vhost",
        exchange = "LogExchange",
        formatter: new JsonFormatter()
    ).CreateLogger();
```

## External configuration using Serilog.Settings.AppSettings

Refer to the [Serilog.Settings.AppSettings](https://github.com/serilog/serilog-settings-appsettings/blob/dev/README.md) package documentation for complete details about sink configuration. 
This is an example of setting some of the configuration parameters for this sink.

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ"/>
<add key="serilog:write-to:RabbitMQ.amqpUri" value="amqp://user:pwd@localhost"/>
<add key="serilog:write-to:RabbitMQ.exchange" value="LogExchange"/>
<add key="serilog:write-to:RabbitMQ.batchPostingLimit" value="1000"/>
<add key="serilog:write-to:RabbitMQ.period" value="00:00:30"/>
```

## External configuration using Serilog.Settings.Configuration

Refer to the [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration/blob/dev/README.md) package documentation for complete details about sink configuration. 

Keys and values are not case-sensitive. This is an example of configuring the sink arguments.

```json
{
  "Serilog": {
    "Using":  ["Serilog.Sinks.RabbitMQ"],
    "MinimumLevel": "Debug",
    "WriteTo": [
      { "Name": "RabbitMQ", 
        "Args": { 
            "amqpUri": "amqp://user:pwd@localhost",
            "exchange": "LogExchange",
            "autoCreateExchange": true,
            "batchPostingLimit": 1000,
            "period": 30,
        } 
      }
    ]
  }
}
```

## Audit Sink Configuration

A Serilog audit sink writes log events which are of such importance that they must succeed, and that verification of a successful write is more important than write performance. 
Unlike the regular sink, an audit sink _does not_ fail silently it can throw exceptions. 
You should wrap audit logging output in a `try/catch` block. The usual example is bank account withdrawal events a bank would certainly not want to allow a failure to record those transactions to fail silently.

The constructor accepts most of the same arguments, and like other Serilog audit sinks, you configure one by using `AuditTo` instead of `WriteTo`.

* `amqpUri`
* `hostanme`
* `username`
* `password`
* `port`
* `vHost`
* `exchange`
* `exchangeType`
* `deliveryMode`
* `routeKey`
* `formatter`

The _batchPostingLimit_ and _period_ parameters are not available because the audit sink writes log events immediately.

## Multihost Configuration

The sink can be configured taking multiple hostnames.  
To keep the _Serilog.Setting.ApSettings_ external configuration, additional hosts are added to the `hostname` argument separated by commas.
This is an example of configuring the multihost using _Serilog.Settings.AppSettings_.

```xml
<add key="serilog:using:RabbitMQ" value="Serilog.Sinks.RabbitMQ"/>
<add key="serilog:write-to:RabbitMQ.hostname" value="host1,host2"/>
<add key="serilog:write-to:RabbitMQ.username" value="user"/>
<add key="serilog:write-to:RabbitMQ.pasword" value="pwd"/>
```

## Version 3.0.0 configuration

There are multiple ways for configuring the RabbitMqSink with the release of v3.0.0

```csharp
var logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) => {
        clientConfiguration.Username     = _config["RABBITMQ_USER"];
        clientConfiguration.Password     = _config["RABBITMQ_PASSWORD"];
        clientConfiguration.Exchange     = _config["RABBITMQ_EXCHANGE"];
        clientConfiguration.ExchangeType = _config["RABBITMQ_EXCHANGE_TYPE"];
        clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
        clientConfiguration.RouteKey     = "Logs";
        clientConfiguration.Port         = 5672;

        foreach (string hostname in _config["RABBITMQ_HOSTNAMES"]) {
            clientConfiguration.Hostnames.Add(hostname);
        }

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

foreach (string hostname in _config["RABBITMQ_HOSTNAMES"]) {
    config .Hostnames.Add(hostname);
}

var logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) => {
    clientConfiguration.From(config);
    sinkConfiguration.TextFormatter = new JsonFormatter();
}) .CreateLogger();
```

```csharp
// Or
var logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) => {
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
        .AddSerilog()
        .AddConsole(LogLevel.Information);

      services.AddSingleton<ILoggerFactory>(loggerFactory);
```

## References

- [Serilog](https://serilog.net/)
- [Logging in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
- [Dependency Injection in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
