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
    /// Default is 50.
    /// </summary>
    public int BatchPostingLimit { get; set; } = LoggerConfigurationRabbitMQExtensions.DEFAULT_BATCH_POSTING_LIMIT;

    /// <summary>
    /// The time to wait between checking for event batches.
    /// Default is 2 seconds.
    /// </summary>
    public TimeSpan BufferingTimeLimit { get; set; } = LoggerConfigurationRabbitMQExtensions._defaultBufferingTimeLimit;

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
    /// Default is <see cref="LogEventLevel.Verbose"/>.
    /// </summary>
    public LogEventLevel RestrictedToMinimumLevel { get; set; } = LogEventLevel.Verbose;

    /// <summary>
    /// Specifies how failed emits should be handled.
    /// </summary>
    public EmitEventFailureHandling EmitEventFailure { get; set; }

    /// <summary>
    /// Validate this configuration. Throws if any required value is missing or out of range.
    /// Idempotent and safe to call multiple times.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <see cref="TextFormatter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="BatchPostingLimit"/>, <see cref="BufferingTimeLimit"/>, or <see cref="QueueLimit"/> is out of range.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3928:Parameter names used into ArgumentException constructors should match an existing one",
        Justification = "Validating instance properties: paramName refers to the property, not a method parameter.")]
    public void Validate()
    {
        if (TextFormatter is null)
        {
            throw new ArgumentException("TextFormatter cannot be null.", nameof(TextFormatter));
        }

        if (BatchPostingLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchPostingLimit), "BatchPostingLimit must be greater than zero.");
        }

        if (BufferingTimeLimit < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BufferingTimeLimit), "BufferingTimeLimit must be non-negative.");
        }

        if (QueueLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueLimit), "QueueLimit, when set, must be greater than zero.");
        }
    }
}
