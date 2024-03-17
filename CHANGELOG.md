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
