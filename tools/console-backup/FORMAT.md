# The console backup file (`ccs-console-backup/1`)

A console backup is **one standard ZIP file** made by CCS Console Workbench (in Classic Cart Studio for Windows, or the standalone
`CCS-Console-Workbench.exe`; earlier versions: Console Backup & Restore and the CCS Console Backup & Restore Utility, whose backups are the
same format and are read as before). It holds everything on one NES Classic's built-in storage at one moment, and it is the file both programs open directly to
restore that console later: nothing has to be unpacked, converted or rebuilt first. Unpacking it with an ordinary ZIP program is fine for
looking at it; it does not restore a console.

**Scope:** the console's built-in storage only. USB or SD storage connected to the console is not included, and nothing in this format can
write to it. The console's memory (RAM), NAND wear and flash-translation tables are not software and are not part of a backup.

Reader and writer: `CcsBackup/Archive` (ZipWriter, ZipReader, BackupFormat, Verifier). A `.zip` extension never makes a file valid: only a
file that passes every check below is a console backup.

## Container

- A standard ZIP. Members are *stored* (the storage images: so they can be read in place) or *deflated* (the small text members). No
  encryption, no folders, no links, no data descriptors, no comment, no bytes before the first member or after the end record.
- ZIP64 records are used when a size, an offset or the member count needs them (a console backup is about 1 GB, so normally not).
- Member names are fixed ASCII paths (below); each member is a regular file with Unix mode 0644.
- The members tile the file from offset 0 to the member index in order; the manifest is the last member. In a backup that has repair
  information (every backup made since it was added), `repair/descriptor.json` is the first member and `repair/chunk-map.json` comes just
  before the manifest (see "Repair information").

## Members

| Path | Role | What it is |
|---|---|---|
| `restore/boot2-active.img` | restore | The startup software in use (the kernel boot image read with `sunxi-flash read_boot2`) |
| `restore/boot2-slot68.img` | restore | Hakchi's spare startup slot (`read_boot2 68`), when it holds an image |
| `restore/nanda.img` … `restore/nandX.img` | restore | Every logical storage part the console's own table lists (boot, system, data, private, user storage), each read whole through the console's NAND driver with nothing mounted |
| `recovery/original-kernel.img` | recovery | The genuine original Nintendo kernel, on its own (the same bytes as the restore member it came from), when it was found |
| `reference/nand-physical.bin` | reference | The physical NAND, 536,870,912 bytes (`sntool sunxi_flash phy_read 0`). Evidence only: it also holds older versions of blocks and is never written to a console |
| `reference/boot0.bin`, `reference/boot1.bin` | reference | The two bootloaders. Never written |
| `reference/nand-logical-head.bin` | reference | The logical device `/dev/nand`. Never written |
| `reference/partition-table.txt` | reference | The storage table as the flash tool prints it |
| `inventory/data-files.json` | inventory | The data storage's file list, read on the PC from the captured image: every entry with its type, permissions, owner, group, size, time, link count, inode, link target and (regular files) SHA-256, and who it belongs to by path (Classic Cart Studio, hakchi, Nintendo, other) |
| `CCS/Console-HOME.ccs` | export | A convenience copy of the console's Classic Cart Studio HOME as an ordinary `.ccs` collection package (tools/companion/CCS-FORMAT.md), when there is one |
| `README.txt` | text | What the file is, in plain words |
| `repair/descriptor.json` | repair | First (backups made since repair information was added). An early copy of the member list for repair: see "Repair information" |
| `repair/chunk-map.json` | repair | Just before the manifest (same backups). A second copy of the member records and piece checksums: see "Repair information" |
| `ccs-console-backup.json` | manifest | Last. Lists every other member with its size, SHA-256 and MD5 |

A restore writes only `restore/` members, and only to the same console with the same storage table and the same bootloaders. The export
is never used by a restore; it is opened with Restore a Backup like any `.ccs` file.

## Manifest

```json
{
  "format": "classic-cart-studio-console-backup",
  "schema": "ccs-console-backup/1",
  "backup_id": "20261114-173512-1a2b3c4d",
  "simulated": false,
  "created_utc": "2026-11-14T17:35:12Z",
  "completed_utc": "2026-11-14T17:47:03Z",
  "scope": {"kind": "internal", "label": "Internal Console Backup",
            "text": "Backs up your console's built-in storage. Games and saves on USB or SD storage are not included.",
            "external_storage": ["No use of USB or SD storage was found in the captured settings."],
            "not_included": ["USB or SD storage connected to the console (left untouched by backup and restore)", "…"]},
  "tool": {"name": "CCS Console Workbench", "version": "1.0.0", "engine": "CcsBackup 1.0.0.0", "entry": "workbench",   (earlier: "CCS Console Backup & Restore Utility", "utility")
           "recovery_image": {"id": "ccs-backup/1/0123456789abcdef", "sha256": "…"}, "transport": "clovershell"},
  "console": {"model": "NES Classic Edition", "board": "dp-nes", "region": "EUR_USA", "oem_version": "release-v1.0.7-0-g4ea4041",
              "hwid": "00000000 00000000 00000000 00000000", "tag": "12345678"},
  "layout": {"nandinfo": "2048 64 4096", "partition_table": "mbr:mbr:32 nanda:boot:6144 nandb:rootfs:38912 nandc:data:806912 nandd:private:2048 nande:UDISK:26624",
             "partitions": [{"device": "nanda", "label": "boot", "bytes": 3145728}, "…"], "kernel_view_kib": "nand:16384,nanda:3072,…"},
  "state": {"kind": "stock", "text": "Original Nintendo software (release v1.0.7).", "active_md5": "…", "active_size": 2820096, "active_hakchi_entries": 0,
            "slot68": {"present": false, "md5": null, "size": 0}, "original_kernel": true, "original_source": "active",
            "original_release": "kernel-dp-nes-release-v1.0.7-0-g4ea4041", "relationship_confirmed": true,
            "relationship": {"active_block": 48, "slot68_block": null, "confirmed": true}},
  "data_partition": {"summary": {"readable": true, "filesystem": "ext4", "condition": "clean", "entries": 35, "files": 16, "…": "…"}},
  "export": {"status": "none", "reason": "There is no Classic Cart Studio HOME on this console."},
  "restore_ready": true, "recovery_baseline": true, "recovery_baseline_missing": [],
  "duplication": {"note": "…", "restore_and_recovery_bytes": 446000000, "physical_reference_bytes": 536870912},
  "members": [{"path": "restore/boot2-active.img", "role": "restore", "target": "boot2", "size": 2820096, "sha256": "…", "md5": "…",
               "console_md5": "…", "console_bytes": 2820096, "source": "sunxi-flash read_boot2 (the startup software in use)"}, "…"]
}
```

This is a synthetic example: the identity, sizes and checksums are placeholders, not a real console's.

- `state.kind`: `stock` (the original Nintendo kernel running and hakchi's spare slot empty) or `modified`. A modified console's running
  kernel is never called damaged; its original kernel, when hakchi kept one in the spare slot, is `recovery/original-kernel.img`.
- `restore_ready`: every restore member is present, valid and matches the recorded layout. `recovery_baseline`: restore-ready AND the genuine
  original kernel is in the backup AND the startup software was found again in the physical image. Classic Cart Studio's maintenance
  changes need a recovery baseline of the console; a backup without one is kept and can still be restored, but it is never treated as the
  console's original.
- `simulated`: made from a simulated console (files on a PC standing in for one, for tests and demonstrations). Never accepted for a real
  console.
- `ccs_target` (optional, `ccs-target/1`): the console model, console software and storage layout the backup is for.
  Backups are written without it, which means the NES Classic Edition; a backup that names anything this version does not support (the
  reserved SNES Classic entries included) fails verification.

## Checks (every time the file is used)

The file is checked from scratch when it is made, when it is opened, before a restore and before every maintenance admission. A record, a
file name, an earlier result or a field in the manifest is never taken as proof:

1. the container rules above (any other ZIP is refused before anything in it is used);
2. the manifest's `format` and `schema`, and its member list equal to the file's members;
3. every member read in full: its size, CRC-32, SHA-256 and MD5 against the manifest, with a hard stop at its declared size and a limit for
   every member and for the whole file;
4. the facts recomputed from the bytes: boot images are Android boot images of the size their header describes; the storage parts match
   the recorded layout; the original kernel member is a genuine stock kernel (Hakchi2 CE 3.9.3's list of NES Classic kernels, and its
   SHA-256 where known) equal to the member it came from; the startup software is found again in the physical image; restore-ready and
   recovery-baseline are decided here and must equal the manifest's;
   and, when the backup has repair information: both repair members present, the descriptor first and stored, both readable with a
   matching `check_sha256`, naming this backup and console, and every record in them equal to the file (each member's place, exact local
   header, CRC-32, sizes, SHA-256, MD5, and the SHA-256 of every piece, recomputed). A backup without repair information is checked exactly
   as before and stays valid;
5. the whole file's SHA-256, read again from the start.

## Repair information

Backups made since repair information was added carry two extra members so that a damaged copy can still be identified, checked piece by
piece and repaired from what the backup recorded about itself. They are additions under the same schema (`ccs-console-backup/1`): every
earlier backup stays valid without them. A reader that predates them refuses a backup that has them (its member names are not on that
reader's list), so such backups need this version or later.

- `repair/descriptor.json` is the **first** member: stored, placed when the capture starts and filled in when it ends, 400,000 bytes (the
  JSON padded with spaces and a final newline; a backup of the largest size the format allows still fits). It lists every member except
  itself and the manifest, so a file whose end (the manifest and the member index) is lost can still be identified and its parts checked.
- `repair/chunk-map.json` is deflated, just before the manifest. It lists every member written before it except the descriptor: a second
  copy of the same records that a damaged start of the file cannot take.

Both are one JSON document (`"format": "classic-cart-studio-console-backup-repair-map"`, `"schema": "ccs-console-backup-repair/1"`,
`"role": "descriptor"` or `"chunk-map"`): the backup's `backup_id`, `simulated`, `created_utc`, `completed_utc`, `console` (`board`,
`hwid`, `tag`), `zip` (`dos_time`, `dos_date`, `zip64`: the time stamp every local header carries and whether ZIP64 local fields were
forced), `chunk_size` (always 1048576) and `members`, in file order, each with `path`, `header_offset`, `data_offset`, `method`,
`crc32` (8 hex digits), `size`, `compressed_size`, `sha256`, `md5`, `local_header` (the member's exact local header bytes, hex) and
`chunks` (the SHA-256 of each 1,048,576-byte piece of the member's stored bytes, the last piece shorter; compressed bytes for a deflated
member), and `check_sha256`.

`check_sha256` is the SHA-256 of this UTF-8 text, each line ending in a line feed: the schema; the role; `backup_id`; `simulated` as
`simulated` or `console`; `board`; `hwid`; `tag`; `created_utc`; `completed_utc`; `dos_time dos_date zip64 chunk_size` (zip64 as 1 or 0);
then for each member `path|header_offset|data_offset|method|crc32|size|compressed_size|sha256|md5|local_header` followed by one line
` <index> <piece sha256>` per piece (decimal numbers, lowercase hex). It lets a copy whose manifest is gone prove its map is not damaged.

Nothing here hashes the whole file or itself (nothing circular): the manifest lists both repair members with their SHA-256 like any member,
and the whole file's SHA-256 is only ever recorded outside it (the operation record). A map says what the bytes were; it never stands in
for checking them: every use recomputes the hashes, and a repair re-hashes every byte it takes.

**Piece size: 1,048,576 bytes (2^20).** The physical image and every partition of the console's original layout are whole multiples of it,
and every partition starts on a whole NAND block, so a damaged piece names whole console storage: eight 131,072-byte NAND erase blocks, or
256 ext4 blocks of 4,096 bytes (a partition resized by other software may end in a shorter piece). It is a multiple of the 4,096-byte disk sector. A 1 GB backup has about 1,000 pieces, a
map of about 70 KB. A parity set added later can use slices of the same size; the repair report gives every damaged range in absolute
file offsets for it.

What uses it (`CcsBackup/Repair`, the UI's repair routes). Each makes a NEW file or folder and only reads the files it is given:

- **Rebuild the table of contents** (`ZipIndexRecovery`): parts are found through the intact index, else the descriptor's records, else a
  lenient read of the index, else a walk through the part headers from the start with checked lengths (a "PK" pattern inside a part is
  never read as a part); names off the list, duplicates and overlaps are refused.
- **Second copy** (`SecondCopy`): a good backup checked in full, copied, read back and compared; the copy's physical drive is reported as
  same, different or "could not be determined". **Merge** (`Merge`): one new good file from two damaged copies of the same backup, each
  piece from whichever copy matches its recorded checksum.
- **Other backups of the same console** (`Donor`): a part or piece is taken from another backup only when its bytes there have exactly
  this backup's recorded checksum and length (a newer save is never used to repair an older one). A storage image is never rebuilt from
  the files inside it.
- **Salvage** (`Salvage`): exports, into a folder that is never a backup, every part whose recorded checksum still matches and every file
  on the data storage whose checksum in `inventory/data-files.json` matches (that list itself verified); the backup stays damaged.

A repaired file must pass every check above from scratch before it gets its name. Its parts are proven by their own checksums; it is
byte-for-byte the original file only when its SHA-256 equals the one recorded when the backup was made (the report says which), and
otherwise it is a new file with its own identity.

## On the PC

- Backups are written as `NES Classic Backup <date> <time> (console <tag>).zip.partial`, checked, and only then renamed to `.zip`, never
  over another file. A second copy is written, read back in full and compared before it gets its name. A `.partial` file is never used.
- Each operation keeps a record in `<backup folder>\.ccs-records\` (history, not authority).
- The Backup Browser (`CcsBackup/Browse`) only reads backups. Beside the records it keeps `browser-checks.json` (when each backup file
  was last checked on this PC, and the result) and, for a backup whose file list did not record every file's checksum (files above
  64,000,000 bytes), `derived-index-<first 16 hex of the backup's SHA-256>.json`: those checksums worked out from the verified backup,
  bound to its SHA-256 and its data storage's. Neither is ever written into a backup or trusted on its own.
- It also lists what is inside the parts that hold other files, read in place: a SquashFS system image (gzip or xz) on the data storage
  or as a storage part, identified by hakchi's own rules, and `CCS/Console-HOME.ccs`'s files (with the startup picture and HOME design
  its `collection.json` carries). A file inside a system image has no recorded checksum (an image keeps none per file): its checksum is
  worked out on this PC through the image, only when the image's own checksum is trusted, and is bound to it (the export list's
  `checksum_bound_to`: the parent's path and SHA-256). A package file has the checksum its own `ccs-manifest.json` recorded. A backup
  whose table of contents is damaged is opened through the repair part finder (`Repair/Examination.cs`), read only; the check log keeps
  how its parts were found.
- The startup images a backup holds (`restore/boot2-active.img`, `restore/boot2-slot68.img`, `recovery/original-kernel.img`) are Android
  boot images: containers of a kernel and a ramdisk (and a second stage when there is one), not bootloaders and not themselves file
  systems. The browser lists each part, and the files of a ramdisk compressed with LZO (the NES Classic's own kernels), gzip or nothing
  (its cpio "newc" archive; `CcsConsole/RamBoot/StartupFiles.cs`, `Lzo.cs`). Other ramdisk compressions are named, not decoded.
- The console's own system part (the `rootfs` part, `restore/nandb.img`) is stored encrypted, as the console stores it. Both hakchi's
  pinned `/init` and the original Nintendo one open it with `cryptsetup open --type plain --cipher aes-xts-plain --key-file key-file`, the
  key coming from a `hakchi-key-file=base64:` word on the command line or a `key-file` in a stored ramdisk, so a backup's startup images
  usually carry it. The browser looks in each of them (the command line first), decrypts the part on the PC through a read-only view
  (`Device/XtsView.cs`: AES-128-XTS with the 32-byte key file as it is, 512-byte sectors numbered from the part's start, the "plain" IV)
  and lists it like any system image only when what the key decrypts it to is a valid SquashFS file system. A key is taken only from a
  startup image that matches its checksum, and the part is decrypted only when it does too. The key stays in memory: it is never shown,
  recorded or put in an export list. The ramdisk's separate `key-file` is listed as private and exported only when it is itself selected;
  a folder, a type, a search or a game skips it (listed under `withheld`). That does not take the key out of anything else: whole startup
  images, their ramdisks and the raw NAND images in `reference/` are exported exactly as the backup holds them and can still contain it,
  and the export's summary and its list's `system_key` say so, never that an export holds no key. Files inside the decrypted part have checksums worked out on the PC, bound to the
  part's recorded SHA-256 and to the startup image the key came from (`checksum_bound_to.decrypted_with_key_from`,
  `key_source_sha256`). Without a key that opens it, the part stays one exact image with each startup image's reason. Nothing decrypted
  is written anywhere, and browsing never makes a restore point. (Freezes 5 to 8 stated that the key was in no backup; that was wrong:
  freeze 9 corrected it.)
- A repair writes `<name> (repaired <date> <time>).zip` the same way (`.zip.partial` first, checked, then renamed, never over a file;
  deleted on cancel or failure), a second copy keeps the backup's name in its own folder, and a salvage makes a new folder
  `<name> (salvaged files <date> <time>)`. Each writes its report beside what it made (`.json` and `.txt`).
- Backups are private: they contain the console's identity and everything on it. Classic Cart Studio never uploads them.
