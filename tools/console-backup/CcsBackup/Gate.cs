// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The physical boundaries, in code: not a setting, not an environment variable, not an argument, not a request field. Each is opened by
// a reviewed code change released as its own milestone. This version opens every one of them: a real console can be read and, after its
// own checks, reviewed plan and confirmation, written; the USB driver can be set up; a USB drive or SD card can be imaged and restored.
// Opening a boundary removes no check: every operation keeps its identity, backup, plan, confirmation and read-back rules.
//   ConsoleSessions      connecting to a real console at all: the FEL RAM boot of a Classic Cart Studio recovery image and the
//                        clovershell link to it. Backup needs this one alone; every other image kind also needs its own write boundary
//                        below (RecoverySessions.Refusal).
//   RestoreWrites        writing a console's storage to put back a captured state (G3).
//   PreparationWrites    the first-time preparation of a console (G1).
//   ReturnWrites         returning a console Classic Cart Studio prepared to its original software (Return to Original Software:
//                        hakchi's own uninstall steps, checked; the data storage then comes back by a restore). Its own boundary: an
//                        open preparation boundary never opens it.
//   RescueWrites         Console Rescue: writing this console's own boot0 or boot1 back from its verified backup. Its own
//                        boundary; no other boundary opens it.
//   ExpertWrites         Expert Mode inside Console Browser: user files (game files, saves, artwork, settings) changed under a session
//                        restore point. Its own boundary.
//   ComposerWrites       Console Composer: chosen saves, settings, hakchi games and Classic Cart Studio data from verified backups onto this
//                        console's data storage, under the strict policy (a backup in the same session). Its own boundary.
//   CleanupWrites        the Sega Mini menu pictures clean-up: removing the eligible pictures for the Sega Mini menu Hakchi2 CE made in
//                        hakchi's game folders, and putting exactly those files back (SegaCleanup), under the strict policy (a backup in
//                        the same session). Its own boundary.
//   DeveloperWrites      Developer Mode: writable file areas, developer kernel/system images under a session restore point. Its own
//                        boundary; custom boot0/boot1/partition-table writes are not part of it (they do not exist in this version).
//   DeveloperTestBoot    Developer Mode's test boot: a startup image that is not a Classic Cart Studio console program, sent to the
//                        console's memory over FEL and started (DeveloperTestBoot.OpenReal). Nothing is written to storage by Workbench,
//                        but the image's own code controls the console once it runs. Its own boundary; DeveloperWrites never opens it.
//   FirmwareWrites       Console Composer's Browse for Firmware: an owner's system image (.hsqs) written to CCS Nucleus's own folder and
//                        added to (or removed from) its start choices (ccsprep rput, rimport, rdrop)
//   MethodWrites         CCS Mod / CCS Nucleus after preparation: removing one, or changing from one to the other, by placing a package or
//                        a removal list in hakchi's transfer folder (MethodEngine; hakchi applies it at the next start). Its own boundary;
//                        installing a method as part of a preparation is under PreparationWrites.
//   DriverChanges        this PC, not the console: setting up or removing Workbench's USB driver package and its certificate (the elevated
//                        --driver-action mode, Driver/DriverActionHost.cs). Its own boundary; it opens no console boundary and none opens it.
//   StorageRawReads      this PC, not the console: reading a connected USB drive or SD card whole, sector by sector, for a whole drive
//                        image (Windows needs administrator permission for that; External/RawDisk.cs). Its own boundary. Files and folders
//                        backups read through ordinary Windows file access and need none; a drive image FILE the person chooses needs none.
//   StorageDiskWrites    this PC, not the console: writing a whole drive image back onto a connected USB drive or SD card (a destructive
//                        restore of the whole drive; External/ExternalRestore.cs). Its own boundary; a drive image FILE as the target needs none.
//   CommunityWrites      Community Mods: a listed author's package placed in hakchi's transfer folder or named in its removal list, and the
//                        mod's settings kept or put back in Classic Cart Studio's own folder (CommunityEngine, ram/ccscomm). Its own boundary;
//                        MethodWrites never opens it.
// A restore, a preparation or a return needs ConsoleSessions AND its own write boundary; RecoveryConsole asks the write boundary again before
// every write command (WriteAuthority: the boundary, the reviewed plan's targets, the same console). With a constant false, the compiler
// leaves the code behind it out (CS0162). A simulated console (a folder of files on the PC, SimulatedConsole) is not behind these boundaries: it can only ever
// change files on the PC, and every backup it makes is marked simulated and refused for a real console.
using System;

namespace CcsBackup
{
    public static class Gate
    {
        public const bool ConsoleSessions = true;    // reading a real console (recovery image in RAM): open in this version
        public const bool RestoreWrites = true;      // G3: writes that put back a captured state: open in this version
        public const bool PreparationWrites = true;  // G1: first-time console preparation writes: open in this version
        public const bool ReturnWrites = true;       // returning a CCS-prepared console to its original software: open in this version
        public const bool RescueWrites = true;       // Console Rescue boot0/boot1 writes: open in this version
        public const bool ExpertWrites = true;       // Expert Mode user-file changes: open in this version
        public const bool DeveloperWrites = true;    // Developer Mode writes: open in this version
        public const bool DeveloperTestBoot = true;  // Developer Mode's test boot of a startup image in the console's memory: open in this version
        public const bool ComposerWrites = true;     // Console Composer's file composition: open in this version
        public const bool CleanupWrites = true;      // removing or putting back the pictures for the Sega Mini menu on a real console: open in this version
        public const bool MethodWrites = true;       // removing or changing CCS Mod / CCS Nucleus on a real console: open in this version
        public const bool FirmwareWrites = true;     // an owner's system image added to or removed from CCS Nucleus on a real console: open in this version
        public const bool DriverChanges = true;      // setting up or removing the USB driver package and its certificate on this PC: open in this version
        public const bool StorageRawReads = true;    // reading a connected USB drive or SD card whole, for a whole drive image: open in this version
        public const bool StorageDiskWrites = true;  // writing a whole drive image back onto a connected USB drive or SD card: open in this version
        public const bool CommunityWrites = true;    // installing, updating or removing a listed community mod on a real console: open in this version
        public const string SessionsText = "Connecting to a real console is not available in this version.";
        public const string RestoreText = "Restoring a real console is not available in this version.";
        public const string PreparationText = "Preparing a real console is not available in this version.";
        public const string ReturnText = "Returning a real console to its original software is not available in this version.";
        public const string RescueText = "Repairing a real console's startup stages is not available in this version.";
        public const string ExpertText = "Changing files on a real console is not available in this version.";
        public const string DeveloperText = "Developer changes to a real console are not available in this version.";
        public const string DeveloperTestBootText = "Starting a startup image in a real console's memory is not available in this version.";
        public const string ComposerText = "Composing a real console is not available in this version.";
        public const string CleanupText = "Removing or putting back pictures on a real console is not available in this version.";
        public const string MethodText = "Removing or changing CCS Mod or CCS Nucleus on a real console is not available in this version.";
        public const string FirmwareText = "Adding or removing a system image on a real console is not available in this version.";
        public const string DriverText = "Setting up or removing the USB driver on this PC is not available in this version.";
        public const string StorageRawText = "Copying a connected USB drive or SD card as a whole drive image is not available in this version.";
        public const string StorageDiskText = "Writing a whole drive image back onto a connected USB drive or SD card is not available in this version.";
        public const string CommunityText = "Installing, updating or removing community mods on a real console is not available in this version.";
    }

    public sealed class GateClosedException : Exception
    {
        /// <summary>the boundary's own sentence, as a window shows it</summary>
        public readonly string Text;
        public GateClosedException(string text, string what) : base(text + " (" + what + ": closed in code)") { Text = text; }
    }
}
