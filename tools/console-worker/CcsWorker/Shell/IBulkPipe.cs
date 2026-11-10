// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
namespace CcsWorker.Shell
{
    /// <summary>The two bulk endpoints clovershell uses. Read returns 0 on a timeout and throws when the device is gone.</summary>
    public interface IBulkPipe : System.IDisposable
    {
        int Read(byte[] buffer, int offset, int count, int timeoutMs);
        void Write(byte[] buffer, int offset, int count, int timeoutMs);
    }
}
