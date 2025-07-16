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

    public override void WriteLine(string? value)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        _innerWriter.WriteLine($"[{timestamp}] {value}");
    }

    public override void Write(char value)
    {
        _innerWriter.Write(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _innerWriter.Dispose();
        base.Dispose(disposing);
    }
}