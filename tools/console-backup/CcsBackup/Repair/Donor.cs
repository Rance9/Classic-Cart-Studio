// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Repair a damaged backup from other backups of the SAME console (other dates). A part or a 1,048,576-byte piece is taken from another
// backup only when its bytes there have exactly the damaged backup's recorded checksum and length: then they ARE this backup's bytes. A
// part that changed between the two backups (a newer save, a different system) has another checksum and is never used: a newer save is
// not a repair of an older one. Parts kept twice in the same backup (the original kernel and the startup software it came from) are used
// the same way. A storage image is never rebuilt from the files inside it: when one cannot be recovered byte for byte, no file is made,
// and Salvage can export the files that still match their recorded checksums while the image stays unrepaired. Backups of another
// console, and simulated backups for a real one, are never used. Everything is only read (Rebuild.cs makes and checks the new file).
using System.Collections.Generic;

namespace CcsBackup.Repair
{
    public static class Donor
    {
        /// <summary>A new, complete backup file in outputFolder from the damaged target and other backups of the same console (donors).
        /// targetCopies: other copies of the damaged backup itself, if any.</summary>
        public static RepairResult Run(string target, IList<string> donors, string outputFolder, RepairOptions options = null, IList<string> targetCopies = null)
        {
            var copies = new List<string> { target }; if (targetCopies != null) copies.AddRange(targetCopies);
            return Runner.Run("donor", copies, donors ?? new List<string>(), outputFolder, options);
        }
    }
}
