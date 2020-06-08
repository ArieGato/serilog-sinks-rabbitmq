// Copyright 2015 Serilog Contributors
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

using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Raw;
using System;


/// <summary>
/// Configuration class for RabbitMQSink
/// </summary>
namespace Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ
{
    public class RabbitMQSinkConfiguration
    {
        public int BatchPostingLimit { get; set; }
        public TimeSpan Period { get; set; }
        public ITextFormatter TextFormatter { get; set; } = new RawFormatter();
        public LogEventLevel RestrictedToMinimumLevel { get; set; } = LogEventLevel.Verbose;
        public LoggingLevelSwitch LevelSwitch { get; set; }
    }
}
