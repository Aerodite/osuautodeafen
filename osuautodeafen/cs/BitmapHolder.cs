using System;
using Avalonia.Media.Imaging;

namespace osuautodeafen;

public class BitmapHolder : IDisposable
{
    public BitmapHolder(string path)
    {
        CurrentBitmap = new Bitmap(path);
    }

    public Bitmap CurrentBitmap { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~BitmapHolder()
    {
        Dispose(false);
    }

    public void UpdateBitmap(string path)
    {
        CurrentBitmap?.Dispose();
        CurrentBitmap = new Bitmap(path);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) CurrentBitmap?.Dispose();
    }
}