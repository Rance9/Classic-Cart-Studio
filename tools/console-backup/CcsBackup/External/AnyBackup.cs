// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The one place that decides which verifier a backup file gets: a USB and SD storage backup (ExternalFormat.Is) is checked by
// StorageVerifier, everything else by the console backup's Verifier, exactly as before. Used by the steps both kinds share: the verified
// second copy (Repair/SecondCopy.cs), recovery data (Repair/Parity.cs) and the scheduled check (Library/ScheduledCheck.cs). A storage
// backup's result is never restore-ready for a console, and a console backup is never read as a storage backup.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;

namespace CcsBackup.External
{
    public static class AnyBackup
    {
        public static Verification Verify(string path, IDictionary<string, string> expected = null, Func<bool> cancelled = null, Action<long, long> progress = null)
            => ExternalFormat.Is(path) ? StorageVerifier.Verify(path, expected, cancelled, progress).AsVerification() : Verifier.VerifyFile(path, expected, cancelled, progress);
    }
}
