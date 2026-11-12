using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Service.Tests;

internal sealed class TestLog : IAppLog
{
    public List<string> Lines { get; } = [];

    public void Info(string message) => Lines.Add("INFO " + message);

    public void Warn(string message) => Lines.Add("WARN " + message);

    public void Error(string message, Exception? exception = null) => Lines.Add("ERROR " + message + " " + exception?.GetType().Name);
}
