namespace Serilog.Sinks.RabbitMQ.Tests.TestHelpers;

/// <summary>
/// xUnit collection that serialises every test class touching <see cref="Serilog.Debugging.SelfLog"/>.
/// SelfLog is a process-wide static; without serialisation, parallel test classes race for the
/// enabled writer and content assertions become flaky (see issue #282). Every test class that
/// opens a <see cref="SelfLogScope"/> must declare <c>[Collection("SelfLog")]</c>.
/// </summary>
[CollectionDefinition("SelfLog", DisableParallelization = true)]
public sealed class SelfLogCollection
{
}
