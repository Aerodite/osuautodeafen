using System;
using System.IO;

namespace osuautodeafen.cs.Log;

public abstract class LogFileManager
{
    /// <summary>
    ///     Clears the contents of the log file at the specified path.
    /// </summary>
    public static void ClearLogFile(string logFilePath)
    {
        try
        {
            if (File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, string.Empty);
                Console.WriteLine($"Log file at {logFilePath} cleared.");
            }
            else
            {
                Console.WriteLine($"Log file at {logFilePath} does not exist.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to clear log file at {logFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Creates a log file at the specified path if it does not already exist.
    /// </summary>
    public static void CreateLogFile(string logFilePath)
    {
        try
        {
            if (!File.Exists(logFilePath))
            {
                File.Create(logFilePath).Dispose();
                Console.WriteLine($"Log file created at {logFilePath}.");
            }
            else
            {
                Console.WriteLine($"Log file at {logFilePath} already exists.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create log file at {logFilePath}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Initializes logging to the specified file and to Console using DualTextWriter
    /// </summary>
    public static void InitializeLogging(string logFilePath)
    {
        FileStream fileStream = new(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        StreamWriter fileWriter = new(fileStream) { AutoFlush = true };
        DualTextWriter dualWriter = new(Console.Out, new TimestampTextWriter(fileWriter));
        Console.SetOut(dualWriter);
        Console.WriteLine($"[INFO] osuautodeafen started at {DateTime.Now:MM-dd HH:mm:ss.fff}");
    }
}