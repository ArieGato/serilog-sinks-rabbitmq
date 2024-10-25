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
- Default text formatter is `CompactJsonFormatter`. The `RawFromatter` was removed from Serilog.
- Requires `Serilog.Sinks.PeriodicBatching` `4.0`. This could break other sinks that inherit from `PeriodicBatchingSink` from this package. This was obsolete code and it has been removed. See [release notes](https://github.com/serilog/serilog-sinks-periodicbatching/releases/tag/v4.0.0) for more information.

## 8.0.0

### Added support for Serilog 4.0.1

Upgraded the Serilog package. The `Serilog.Sinks.PeriodicBatching` has been replaced by the native Serilog batching mechanism.

### Added support to handle events before the log message is published

Using `ISendMessageEvents` Interface to handle `OnGetRoutingKey` and `OnGetProperties` Events

The `ISendMessageEvents` interface provides a mechanism for handling messaging events, such as `OnGetRoutingKey` and `OnGetProperties`. This is useful for customizing the behavior of message routing and setting specific properties before the message is sent.

Below is a guide on how to implement a class that implements these events and customizes the message routing key and properties:

The `ISendMessageEvents` interface declares two methods:

- OnGetRoutingKey: This event is triggered to retrieve the routing key for the message.
- OnGetProperties: This event is triggered to retrieve additional properties that will be attached to the message.


Step 1: Implement the ISendMessageEvents Interface in a Class
Next, you create a class that implements the ISendMessageEvents interface. This class will handle both the routing key and message properties before the message is sent.

```csharp
/// <summary>
/// A custom SendMessageEvents class to handle events before sending a message.
/// </summary>
public sealed class CustomSendMessageEvents : ISendMessageEvents
{
    private readonly string _defaultRoutingKey;

    /// <summary>
    /// The constructor for the CustomSendMessageEvents class.
    /// </summary>
    /// <param name="defaultRoutingKey">The default routing key.</param>
    public CustomSendMessageEvents(string defaultRoutingKey)
    {
        _defaultRoutingKey = defaultRoutingKey;
    }

    /// <inheritdoc />
    public Func<LogEvent, IDictionary<string, object?>> OnGetProperties => @event =>
    {
        @event.Properties.TryGetValue("messageType", out var messageType);
        return new Dictionary<string, object?>
        {
            { "messageType", messageType?.ToString() },
            { "log-level", @event.Level.ToString() },
        };
    };

    /// <inheritdoc />
    public Func<LogEvent, string> OnGetRoutingKey => @event =>
    {
        // example of routing based on log level
        return @event.Level switch
        {
            LogEventLevel.Error => "error",
            _ => _defaultRoutingKey
        };
    };
}
```

In this example:

`OnGetRoutingKey`: This method checks the log level and determines the appropriate routing key for error logs.

`OnGetProperties`: This method generates a dictionary of properties, such as log-level and message type, which can be passed along with the message.

Step 2: Register the CustomMessageHandler Class with the Logger Configuration

After implementing the ISendMessageEvents interface, you need to register the CustomMessageHandler class with the logger configuration.

```json
"Args": {
  // all other config settings ...
  "sendMessageEvents": {
    "type": "Net8AppsettingsJsonSample.CustomSendMessageEvents, Net8AppsettingsJsonSample",
    "defaultRoutingKey": "logs"
  }

```

### Breaking changes

- Renamed `period` property to `bufferingTimeLimit`.
- Renamed `routekey` property to `routingKey`.
- Removed `Serilog.Sinks.PeriodicBatching` dependency.
- Removed `RouteKeyFunc` delegate from configuration.
