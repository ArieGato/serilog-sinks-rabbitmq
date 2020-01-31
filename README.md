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

To use with `ILoggerFactory` via dependency injection, 
add the following to `ConfigureServices` in your `Startup` class. 
See the [logging documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
for specific help on using the `ILoggerFactory` and `ILogger<T>`.

``` csharp
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Sinks.RabbitMQ;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

public class Startup 
{
   private readonly IConfiguration _config;
   // ... 
   public IServiceProvider ConfigureServices(IServiceCollection services)
   {
      var config = new RabbitMQConfiguration
      {
          Hostname = _config["RABBITMQ_HOST"],
          Username = _config["RABBITMQ_USER"],
          Password = _config["RABBITMQ_PASSWORD"],
          Exchange = _config["RABBITMQ_EXCHANGE"],
          ExchangeType = _config["RABBITMQ_EXCHANGE_TYPE"],
          DeliveryMode = RabbitMQDeliveryMode.Durable,
          RouteKey = "Logs",
          Port = 5672
      };

      Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .WriteTo.RabbitMQ(config, new JsonFormatter())
        .CreateLogger();

      var loggerFactory = new LoggerFactory();
      loggerFactory
        .AddSerilog()
        .AddConsole(LogLevel.Information);

      services.AddSingleton<ILoggerFactory>(loggerFactory);
   }
   // ...
}
```
## Version 3.0.0 configuration

There are multiple ways for configuring the RabbitMqSink with the release of v3.0.0

```csharp
Log.Logger = new LoggerConfiguration()
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

Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) => {
    clientConfiguration.From(config);
    sinkConfiguration.TextFormatter = new JsonFormatter();
}) .CreateLogger();
```

```csharp
// Or
Log.Logger = new LoggerConfiguration()
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
        .AddSerilog() //if you are not assigning the logger to Log.Logger, then you need to add your logger here.
        .AddConsole(LogLevel.Information);

      services.AddSingleton<ILoggerFactory>(loggerFactory);
```

### Dynamic Routing Keys

If you need support for dynamic routing keys you can supply a delegate to build a routing key based on the log event.

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) => {
    clientConfiguration.From(config);
    // The routing key used to publish the message to RabbitMQ will now be
    // the level of the log event (e.g., "Information", "Fatal", etc.).
    sinkConfiguration.BuildRoutingKey = logEvent => logEvent.Level.ToString();
}).CreateLogger();
```

## References

- [Serilog](https://serilog.net/)
- [Logging in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
- [Dependency Injection in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
