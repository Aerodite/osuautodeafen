using System.IO;
using System.Text;

namespace osuautodeafen.cs.Log;

public class DualTextWriter : TextWriter
{
    private readonly TextWriter _consoleWriter;
    private readonly TextWriter _fileWriter;

    public DualTextWriter(TextWriter consoleWriter, TextWriter fileWriter)
    {
        _consoleWriter = consoleWriter;
        _fileWriter = fileWriter;
    }

    public override Encoding Encoding => _consoleWriter.Encoding;

    /// <summary>
    /// Writes Console.Write output to both the console and a file (as a char)
    /// </summary>
    /// <param name="value"></param>
    public override void Write(char value)
    {
        _consoleWriter.Write(value);
        _fileWriter.Write(value);
    }
    
    /// <summary>
    /// Writes Console.Write output to both the console and a file (as a string)
    /// </summary>
    /// <param name="value"></param>
    public override void Write(string? value)
    {
        _consoleWriter.Write(value);
        _fileWriter.Write(value);
    }
    
    /// <summary>
    /// Writes Console.WriteLine output to both the console and a file (as a string)
    /// </summary>
    /// <param name="value"></param>
    public override void WriteLine(string? value)
    {
        _consoleWriter.WriteLine(value);
        _fileWriter.WriteLine(value);
    }

    /// <summary>
    /// Flushes both the console and file writers
    /// </summary>
    public override void Flush()
    {
        _consoleWriter.Flush();
        _fileWriter.Flush();
    }
}