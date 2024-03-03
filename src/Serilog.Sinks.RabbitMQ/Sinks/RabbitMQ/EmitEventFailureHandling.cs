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
/// Specifies options for handling failures when emitting the events to RabbitMQ. Can be a combination of options.
/// </summary>
[Flags]
public enum EmitEventFailureHandling
{
    /// <summary>
    /// Ignore the failure and continue.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// Send the error to the SelfLog.
    /// </summary>
    WriteToSelfLog = 1,

    /// <summary>
    /// Write the events to another sink. Make sure to configure this one.
    /// </summary>
    WriteToFailureSink = 2,

    /// <summary>
    /// Throw the exception to the caller.
    /// </summary>
    ThrowException = 4,
}
