using System;
using System.Diagnostics;
using System.Text;
using System.Windows;

// Captures WPF binding errors during the real dialog interaction tests.
internal sealed class BindingErrorListener : TraceListener
{
    private readonly StringBuilder errors = new StringBuilder();
    public override void Write(string message) { errors.Append(message); }
    public override void WriteLine(string message) { errors.AppendLine(message); }
    public void AssertNoErrors()
    {
        if (errors.Length != 0)
            throw new InvalidOperationException("WPF binding diagnostics:\n" + errors);
    }
    public static BindingErrorListener Start()
    {
        var listener = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        return listener;
    }
}
