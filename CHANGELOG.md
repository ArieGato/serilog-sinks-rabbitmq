# Release Notes

## 7.0.0

### Added support for Serilog 3.1.1

Upgraded the Serilog package to fix the issue with the missing `RawFormatter`.

### Failure sink support

Failure sink(s) can be configured through appsettings.json or via code.

In this example the Console sink is used as a failure sink. If the RabbitMQ sink fails
to emit an event, it will write to the Console sink.

```json
  "Serilog": {
    "Using": [ "Serilog.Sinks.RabbitMQ", "Serilog.Sinks.Console" ],
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "RabbitMQ",
        "Args": {
          "username": "serilog",
          "password": "serilog",
          "hostnames": [
            "localhost"
          ],
          "emitEventFailure": "WriteToSelfLog,WriteToFailureSink",
          "failureSinkConfiguration": [
            {
              "Name": "Console"
            }
          ]
        }
      }
    ]
  }
```

### Improved SSL support

The SSL support has been improved. Now all properties are used when creating the connection.

### Breaking changes

- Renamed `LoggerConfigurationRabbitMqExtension` to `LoggerConfigurationRabbitMQExtensions`.
- Renamed `hostname` to `hostnames` in configuration.
- Default text formatter is `CompactJsonFormatter`. The `RawFormatter` was removed from Serilog.
- Requires `Serilog.Sinks.PeriodicBatching` `4.0`. This could break other sinks that inherit from
`PeriodicBatchingSink` from this package. This was obsolete code and it has been removed. See
[release notes](https://github.com/serilog/serilog-sinks-periodicbatching/releases/tag/v4.0.0)
for more information.

## 8.0.0

### Added support for RabbitMQ.Client 7.0.0

Add support for `RabbitMQ.Client` version `7.0.0`. Note that this is a breaking change.
The `RabbitMQ.Client` `7.0.0` is not compatible with the `RabbitMQ.Client` `6.x`.

### Added support for Serilog 4.2.0

Removed reference to `Serilog.Sinks.PeriodicBatching` and use `Serilog.Core.Sinks.Batching.BatchingSink` instead.

### Set message properties

Add support for setting `BasicProperties` before publishing a message. Properties can be set by creating a class implementing `ISendMessageEvents`.

 ```csharp
public void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties)
{
    // example of setting message headers based on log event properties
    logEvent.Properties.TryGetValue("messageType", out var messageType);
    properties.Headers = new Dictionary<string, object?>
    {
        { "messageType", messageType?.ToString() },
    };

    // example of setting correlation id based on log event properties
    if (logEvent.Properties.TryGetValue(LogProperties.CORRELATION_ID, out var correlationId))
    {
        properties.CorrelationId = correlationId.ToString();
    }
}
```

### Dynamic Routing Key

Moved the logic for determining the routing key logic to the `ISendMessageEvents`.
This allows for more flexibility when setting the routing key.

```csharp
public string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey)
{
    // example of routing based on log level
    return logEvent.Level switch
    {
        LogEventLevel.Error => "error",
        _ => _defaultRoutingKey
    };
}
```

### Added support for .net 9

Add `net9.0` to the target frameworks.

### Breaking changes

- Upgrade RabbitMQ.Client to `7.0.0`.
- Upgrade Serilog to `4.2.0`.
- Renamed `Period` to `BufferingTimeLimit`.
- Renamed `RouteKey` to `RoutingKey`
- Removed `RoutingFunc`
- Removed `.net7.0` target framework

## 9.0.0 [not published]

### Added support for .net 10

Add `net10.0` to the target frameworks.

### Breaking changes

- Removed `net6.0` and `.net9.0` target framework
