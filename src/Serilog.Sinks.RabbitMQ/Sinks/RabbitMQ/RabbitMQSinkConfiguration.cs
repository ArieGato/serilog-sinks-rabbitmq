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

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Initialization options for <see cref="RabbitMQSink"/>.
/// </summary>
public class RabbitMQSinkConfiguration
{
    /// <summary>
    /// The maximum number of events to include in a single batch.
    /// </summary>
    public int BatchPostingLimit { get; set; }

    /// <summary>
    /// The time to wait between checking for event batches.
    /// </summary>
    public TimeSpan Period { get; set; }

    /// <summary>
    /// The batched sink internal queue limit.
    /// </summary>
    public int? QueueLimit { get; set; }

    /// <summary>
    /// Controls the rendering of log events into text, for example to log JSON. 
    /// To control plain text formatting, use the overload that accepts an output template.
    /// </summary>
    public ITextFormatter TextFormatter { get; set; } = new CompactJsonFormatter();

    /// <summary>
    /// The minimum level for events passed through the sink.
    /// </summary>
    public LogEventLevel RestrictedToMinimumLevel { get; set; } = LogEventLevel.Verbose;

    /// <summary>
    /// Specifies how failed emits should be handled.
    /// </summary>
    public EmitEventFailureHandling EmitEventFailure { get; set; }
}
