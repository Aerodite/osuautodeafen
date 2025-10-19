using System.IO;
using System.Text;

namespace osuautodeafen.cs.Log;

public class DualTextWriter(TextWriter consoleWriter, TextWriter fileWriter) : TextWriter
{
    public override Encoding Encoding => consoleWriter.Encoding;

    /// <summary>
    /// Writes Console.Write output to both the console and a file (as a char)
    /// </summary>
    /// <param name="value"></param>
    public override void Write(char value)
    {
        consoleWriter.Write(value);
        fileWriter.Write(value);
    }
    
    /// <summary>
    /// Writes Console.Write output to both the console and a file (as a string)
    /// </summary>
    /// <param name="value"></param>
    public override void Write(string? value)
    {
        consoleWriter.Write(value);
        fileWriter.Write(value);
    }
    
    /// <summary>
    /// Writes Console.WriteLine output to both the console and a file (as a string)
    /// </summary>
    /// <param name="value"></param>
    public override void WriteLine(string? value)
    {
        consoleWriter.WriteLine(value);
        fileWriter.WriteLine(value);
    }

    /// <summary>
    /// Flushes both the console and file writers
    /// </summary>
    public override void Flush()
    {
        consoleWriter.Flush();
        fileWriter.Flush();
    }
}