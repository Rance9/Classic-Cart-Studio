// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Rebuild a damaged table of contents (the ZIP index at the end of the file). The parts are found, in this order, through: the intact
// index itself (then there is nothing to rebuild); the backup's own repair map (its early copy of the part list, with every part's place
// and exact header); a lenient read of an index whose part headers are damaged; a bounded walk through the part headers from the start
// of the file with checked lengths, ZIP64 sizes included (Examination.cs). A "PK" pattern inside a part, such as a storage image, is never
// taken as a part: each header is read only exactly where the previous part ends. Names outside the format's fixed list, duplicate names
// and overlapping parts are refused. Every part must match its recorded checksum; the new file is written under a new name and must pass
// the full check (Rebuild.cs). The damaged file is never changed. Content checks prove the parts, not the old container's bytes: the
// report says whether the new file is byte-for-byte the original (only when the original's recorded checksum is given) or a new
// container with its own identity.
using System.Collections.Generic;

namespace CcsBackup.Repair
{
    public static class ZipIndexRecovery
    {
        /// <summary>A new backup file with a rebuilt table of contents, in outputFolder; the damaged file is only read.</summary>
        public static RepairResult Run(string backup, string outputFolder, RepairOptions options = null) =>
            Runner.Run("index", new List<string> { backup }, new List<string>(), outputFolder, options);
    }
}
