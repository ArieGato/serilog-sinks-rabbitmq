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

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Specifies options for handling failures when emitting the events to RabbitMQ.
/// Can be a combination of options.
/// </summary>
/// <remarks>
/// These flags apply to the batched pipeline used by <c>WriteTo.RabbitMQ(...)</c>.
/// The audit path (<c>AuditTo.RabbitMQ(...)</c>) always propagates publish exceptions —
/// <see cref="WriteToFailureSink"/> and <see cref="WriteToSelfLog"/> have no effect there
/// because audit semantics require failures to surface to the caller.
/// </remarks>
[Flags]
public enum EmitEventFailureHandling
{
    /// <summary>
    /// Let publish exceptions propagate to the surrounding pipeline (for
    /// <c>WriteTo.RabbitMQ</c>, that is <c>BatchingSink</c>'s
    /// <see cref="Serilog.Core.ILoggingFailureListener"/> machinery). This is the default
    /// and composes with <c>WriteTo.Fallback(...)</c>.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// Send the error to the <see cref="Debugging.SelfLog"/> before propagating.
    /// </summary>
    WriteToSelfLog = 1,

    /// <summary>
    /// Legacy: catch publish exceptions and emit the failed events to a separately-configured
    /// failure sink. The exception is not propagated unless combined with
    /// <see cref="ThrowException"/>. Consider <c>WriteTo.Fallback(...)</c> for new code.
    /// </summary>
    WriteToFailureSink = 2,

    /// <summary>
    /// Force the publish exception to be rethrown even when combined with
    /// <see cref="WriteToFailureSink"/>. Redundant on its own — the default path
    /// already rethrows.
    /// </summary>
    ThrowException = 4,
}
