using Serilog.Debugging;

namespace Serilog.Sinks.RabbitMQ.Tests.TestHelpers;

/// <summary>
/// Scoped capture of <see cref="SelfLog"/> output for a single test. Enables SelfLog on
/// construction against a fresh <see cref="StringWriter"/> and disables + disposes on
/// <see cref="Dispose"/>. Replaces the manual <c>SelfLog.Enable(new StringWriter(sb))</c>
/// pattern that leaked writers across tests and made content assertions race-prone.
/// Any test class that uses this helper must carry <c>[Collection("SelfLog")]</c> so the
/// shared global writer is not claimed by a parallel test class.
/// </summary>
internal sealed class SelfLogScope : IDisposable
{
    private readonly StringWriter _writer;
    private bool _disposed;

    public SelfLogScope(out StringBuilder output)
    {
        output = new StringBuilder();
        _writer = new StringWriter(output);
        SelfLog.Enable(_writer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SelfLog.Disable();
        _writer.Dispose();
        _disposed = true;
    }
}
