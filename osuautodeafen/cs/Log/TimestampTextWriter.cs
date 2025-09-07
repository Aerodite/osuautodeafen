using System;
using System.IO;
using System.Text;

public class TimestampTextWriter : TextWriter
{
    private readonly TextWriter _innerWriter;

    public TimestampTextWriter(TextWriter innerWriter)
    {
        _innerWriter = innerWriter;
    }

    public override Encoding Encoding => _innerWriter.Encoding;

    /// <summary>
    ///     Writes a line with a timestamp prefix
    /// </summary>
    /// <param name="value"></param>
    public override void WriteLine(string? value)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        _innerWriter.WriteLine($"[{timestamp}] {value}");
    }

    /// <summary>
    ///     Writes a single character without a timestamp
    /// </summary>
    /// <param name="value"></param>
    public override void Write(char value)
    {
        _innerWriter.Write(value);
    }

    /// <summary>
    ///     Disposes the inner writer if disposing is true
    /// </summary>
    /// <param name="disposing"></param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _innerWriter.Dispose();
        base.Dispose(disposing);
    }
}