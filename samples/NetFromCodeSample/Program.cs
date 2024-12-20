// Copyright 2015-2024 Serilog Contributors
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

using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.RabbitMQ;

SelfLog.Enable(Console.Error);

Log.Logger = new LoggerConfiguration()
    .WriteTo.RabbitMQ(
        (clientConfiguration, sinkConfiguration) =>
        {
            sinkConfiguration.BatchPostingLimit = 100;
            sinkConfiguration.EmitEventFailure = EmitEventFailureHandling.WriteToFailureSink | EmitEventFailureHandling.WriteToSelfLog;
            sinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Information;
            sinkConfiguration.BufferingTimeLimit = TimeSpan.FromSeconds(2);
            sinkConfiguration.TextFormatter = new Serilog.Formatting.Json.JsonFormatter();

            clientConfiguration.AutoCreateExchange = true;
            clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
            clientConfiguration.Exchange = "LogExchange";
            clientConfiguration.ExchangeType = "direct";
            clientConfiguration.Hostnames = ["localhost"];
            clientConfiguration.Password = "serilog";
            clientConfiguration.Username = "serilog";
        },
        failureSinkConfiguration => failureSinkConfiguration.File("./log/log.txt", LogEventLevel.Information))
    .AuditTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
        {
            sinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Information;
            sinkConfiguration.TextFormatter = new Serilog.Formatting.Json.JsonFormatter();

            clientConfiguration.AutoCreateExchange = true;
            clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
            clientConfiguration.Exchange = "LogExchange";
            clientConfiguration.ExchangeType = "direct";
            clientConfiguration.Hostnames = ["localhost"];
            clientConfiguration.Password = "serilog";
            clientConfiguration.Username = "serilog";
        })
    .CreateLogger();

Log.Information("Hello, world!");

Log.CloseAndFlush();
