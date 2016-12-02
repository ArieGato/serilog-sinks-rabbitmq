# serilog-sinks-rabbitmq
<img src="https://ci.appveyor.com/api/projects/status/row7ob6fhgmvtqwb?svg=true" /> 
<a href="https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ/">
  <img src="https://img.shields.io/nuget/v/Serilog.Sinks.RabbitMQ.svg" />
</a>

Serilog Sink for RabbitMq

## Installation

Using [Nuget](https://www.nuget.org/packages/Serilog.Sinks.RabbitMQ/):

```
Install-Package Serilog.Sinks.RabbitMQ
```

## Usage

To use with `ILoggerFactory` via dependency injection, 
add the following to your `Startup` class. 
See the [logging documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
for specific help on using the `ILoggerFactory` and `ILogger<T>`.

```
using Serilog;
using Serilog.Formatting.Json;
using Serilog.Sinks.RabbitMQ;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

public class Startup 
{
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
}
```


## References

- [Serilog](https://serilog.net/)
- [Logging in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging)
- [Dependency Injection in ASP.Net Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)