// CCS Console Workbench native host: PAR2 parity through par2cmdline's libpar2, linked into the host. Copyright (C) 2026 Rance9.
// GPL-3.0-or-later (par2cmdline itself: GPL-2.0-or-later).
//
// The narrow ABI of ccs_host_abi.h on top of libpar2's own classes (Par2Creator, Par2Repairer), used unmodified:
//  - progress: libpar2 writes its progress as text to the std::ostream it is given ("Processing: 12.3%\r"); ProgressBuf reads that text
//    and turns it into (stage, permille) calls. The counts in the report come from the libraries' own fields (subclasses read the
//    protected members after Process), never from parsing text.
//  - cancel: ProgressBuf checks the cancel flag on every write and then throws CancelRequest out of the stream (the stream is set to
//    rethrow); the library's objects are destroyed as the exception unwinds (their destructors close every file), and this layer then
//    removes the files the call created. libpar2 writes progress at least every 50 ms while it works, so a cancel takes effect quickly,
//    except during a few silent steps (building the Reed-Solomon matrix for a large repair).
//  - files: the protected file and the parity files are held open for the whole call with read sharing only, so nothing (libpar2
//    included) can write, rename or delete them meanwhile. libpar2 renames a damaged file in place when it repairs; this layer never lets
//    it near the original: the rebuild happens in a new private folder beside the requested output, with the original given to libpar2
//    only as an extra file to read blocks from.
// No temporary file anywhere else: libpar2 writes only the parity files (and its own "<name>.check.par2" probe, created and deleted in
// the parity folder at the start of a create), and the repair writes only inside its private folder and then the output.
#include "libpar2internal.h"
#include "ccs_host_abi.h"
#include "host_version.h"

#include <atomic>
#include <cwchar>

extern "C" const wchar_t *ccs_host_arch(void);

namespace {

struct CancelRequest {};

enum OpKind { kCreate, kVerify, kRepair };

// receives everything libpar2 writes to its output stream
class ProgressBuf : public std::streambuf {
public:
    ProgressBuf(OpKind op, ccs_par2_progress_fn fn, void *ctx, volatile int32_t *cancel)
        : op_(op), fn_(fn), ctx_(ctx), cancel_(cancel), stage_(op == kCreate ? CCS_PAR2_STAGE_READING_DATA : CCS_PAR2_STAGE_LOADING_PARITY) {}
    void SetStage(int32_t stage, int32_t permille) { stage_ = stage; Report(permille); }
    void CheckCancel() { if (stop_ || (cancel_ && *cancel_)) { stop_ = true; throw CancelRequest(); } }
    bool Stopped() const { return stop_; }
    std::string Log() const { return log_; }
    bool checking_output = false;

protected:
    int overflow(int c) override { if (c != EOF) Put((char)c); return c == EOF ? 0 : c; }
    std::streamsize xsputn(const char *s, std::streamsize n) override { for (std::streamsize i = 0; i < n; i++) Put(s[i]); return n; }
    int sync() override { CheckCancel(); return 0; }

private:
    void Put(char c)
    {
        CheckCancel();
        if (c == '\r' || c == '\n') { Segment(c == '\n'); seg_.clear(); return; }
        if (seg_.size() < 4096) seg_ += c;
    }

    void Segment(bool line)
    {
        std::string s = seg_;
        while (!s.empty() && s[0] == ' ') s.erase(0, 1);
        while (!s.empty() && s.back() == ' ') s.pop_back();
        if (s.empty()) return;
        if (line) { if (log_.size() < 65536) { log_ += s; log_ += '\n'; } }
        // a percentage at the end: "<label>NNN.N%"
        int32_t permille = -1; std::string label = s;
        if (s.back() == '%') {
            size_t i = s.size() - 1, j = i;
            while (j > 0 && (isdigit((unsigned char)s[j - 1]) || s[j - 1] == '.')) j--;
            std::string num = s.substr(j, i - j);
            size_t dot = num.find('.');
            if (!num.empty() && dot != std::string::npos) {
                permille = atoi(num.substr(0, dot).c_str()) * 10 + atoi(num.substr(dot + 1, 1).c_str());
                if (permille > 1000) permille = 1000;
                label = s.substr(0, j);
            }
        }
        int32_t stage = stage_;
        if (Starts(label, "Processing")) stage = op_ == kCreate ? CCS_PAR2_STAGE_COMPUTING : CCS_PAR2_STAGE_REPAIRING;
        else if (Starts(label, "Repairing")) stage = CCS_PAR2_STAGE_REPAIRING;
        else if (Starts(label, "Loading")) stage = CCS_PAR2_STAGE_LOADING_PARITY;
        else if (Starts(label, "Scanning") || Starts(label, "Target") || Starts(label, "Verifying source files"))
            stage = checking_output ? CCS_PAR2_STAGE_CHECKING_OUTPUT : CCS_PAR2_STAGE_SCANNING_DATA;
        else if (Starts(label, "Opening")) stage = CCS_PAR2_STAGE_READING_DATA;
        else if (Starts(label, "Writing recovery packets") || Starts(label, "Writing verification packets")) { stage = CCS_PAR2_STAGE_WRITING; permille = -1; }
        else if (Starts(label, "Verifying repaired files")) { checking_output = true; stage = CCS_PAR2_STAGE_CHECKING_OUTPUT; permille = -1; }
        else if (permille < 0) return;   // other text: kept in the log only
        stage_ = stage;
        Report(permille);
    }

    void Report(int32_t permille)
    {
        if (!fn_ || (stage_ == lastStage_ && permille == lastPermille_)) return;
        lastStage_ = stage_; lastPermille_ = permille;
        if (fn_(ctx_, stage_, permille) != 0) stop_ = true;
        CheckCancel();
    }

    static bool Starts(const std::string &s, const char *p) { return s.compare(0, strlen(p), p) == 0; }

    OpKind op_;
    ccs_par2_progress_fn fn_; void *ctx_; volatile int32_t *cancel_;
    int32_t stage_, lastStage_ = -2, lastPermille_ = -2;
    bool stop_ = false;
    std::string seg_, log_;
};

// keeps the last error lines libpar2 writes
class ErrorBuf : public std::streambuf {
public:
    std::string text;
protected:
    int overflow(int c) override { if (c != EOF && text.size() < 16384) text += (char)c; return c == EOF ? 0 : c; }
    std::streamsize xsputn(const char *s, std::streamsize n) override { for (std::streamsize i = 0; i < n; i++) overflow(s[i]); return n; }
};

// the libraries' own counts, read from their protected members after Process
class CcsRepairer : public Par2Repairer {
public:
    using Par2Repairer::Par2Repairer;
    u32 BlockSize() const { return (u32)blocksize; }
    u32 SourceBlocks() const { return sourceblockcount; }
    u32 Available() const { return availableblockcount; }
    u32 Missing() const { return missingblockcount; }
    u32 RecoveryAvailable() const { return (u32)recoverypacketmap.size(); }
    bool HasMain() const { return mainpacket != nullptr; }
    u32 Recoverable() const { return mainpacket ? mainpacket->RecoverableFileCount() : 0; }
    u32 Complete() const { return completefilecount; }
    std::vector<std::string> TargetNames() const
    {
        std::vector<std::string> v;
        for (auto *f : sourcefiles) if (f) v.push_back(f->TargetFileName());
        return v;
    }
};

class CcsCreator : public Par2Creator {
public:
    using Par2Creator::Par2Creator;
};

// ---------- helpers ----------

std::atomic<bool> g_busy(false);

struct BusyGuard {
    bool ok;
    BusyGuard() { bool expected = false; ok = g_busy.compare_exchange_strong(expected, true); }
    ~BusyGuard() { if (ok) g_busy.store(false); }
};

std::string Utf8(const std::wstring &w)
{
    std::string s;
    utf8::WideToUtf8(w, s);
    return s;
}

std::wstring Wide(const std::string &s)
{
    if (s.empty()) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), NULL, 0);
    std::wstring w((size_t)n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &w[0], n);
    return w;
}

std::wstring FullPath(const wchar_t *p)
{
    if (!p || !*p) return std::wstring();
    DWORD n = GetFullPathNameW(p, 0, NULL, NULL);
    if (!n) return std::wstring();
    std::wstring w((size_t)n, L'\0');
    n = GetFullPathNameW(p, n, &w[0], NULL);
    w.resize(n);
    return w;
}

std::wstring Folder(const std::wstring &path) { size_t i = path.find_last_of(L"\\/"); return i == std::wstring::npos ? std::wstring() : path.substr(0, i + 1); }
std::wstring Name(const std::wstring &path) { size_t i = path.find_last_of(L"\\/"); return i == std::wstring::npos ? path : path.substr(i + 1); }

bool IsFile(const std::wstring &p, uint64_t *size = nullptr)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &a) || (a.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) return false;
    if (size) *size = ((uint64_t)a.nFileSizeHigh << 32) | a.nFileSizeLow;
    return true;
}

bool IsFolder(const std::wstring &p)
{
    DWORD a = GetFileAttributesW(p.c_str());
    return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY);
}

bool Exists(const std::wstring &p) { return GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES; }

bool EndsWithI(const std::wstring &s, const wchar_t *tail)
{
    size_t n = wcslen(tail);
    return s.size() >= n && _wcsicmp(s.c_str() + s.size() - n, tail) == 0;
}

// the parity set's files: "<base>.par2" and "<base>.vol*.par2" in base's folder (base = the index path without ".par2")
std::vector<std::wstring> ParityFiles(const std::wstring &base, bool include_check = false)
{
    std::vector<std::wstring> v;
    std::wstring folder = Folder(base);
    if (IsFile(base + L".par2")) v.push_back(base + L".par2");
    if (include_check && IsFile(base + L".check.par2")) v.push_back(base + L".check.par2");
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((base + L".vol*.par2").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) v.push_back(folder + fd.cFileName); } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    return v;
}

// read-only handles that forbid writing, renaming and deleting the file while the call runs
class Holds {
public:
    ~Holds() { for (HANDLE h : h_) CloseHandle(h); }
    bool Add(const std::wstring &p, DWORD *error)
    {
        HANDLE h = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
        if (h == INVALID_HANDLE_VALUE) { *error = GetLastError(); return false; }
        h_.push_back(h); return true;
    }
private:
    std::vector<HANDLE> h_;
};

std::wstring Thousands(uint64_t v)
{
    std::wstring s = std::to_wstring(v), o;
    int c = 0;
    for (size_t i = s.size(); i-- > 0;) { o.insert(o.begin(), s[i]); if (++c % 3 == 0 && i > 0) o.insert(o.begin(), L','); }
    return o;
}

std::wstring Bytes(uint64_t v)   // decimal units
{
    wchar_t b[64];
    if (v >= 1000000000ull) swprintf(b, 64, L"%.2f GB", v / 1e9);
    else if (v >= 1000000ull) swprintf(b, 64, L"%.1f MB", v / 1e6);
    else if (v >= 1000ull) swprintf(b, 64, L"%.1f KB", v / 1e3);
    else swprintf(b, 64, L"%llu bytes", (unsigned long long)v);
    return b;
}

std::wstring Win32Error(DWORD e)
{
    wchar_t buf[512] = L"";
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, e, 0, buf, 512, NULL);
    std::wstring s = buf;
    while (!s.empty() && (s.back() == L'\n' || s.back() == L'\r' || s.back() == L' ' || s.back() == L'.')) s.pop_back();
    return s;
}

std::wstring LastLine(const std::string &text)
{
    std::string t = text;
    while (!t.empty() && (t.back() == '\n' || t.back() == '\r' || t.back() == ' ')) t.pop_back();
    size_t i = t.find_last_of('\n');
    return Wide(i == std::string::npos ? t : t.substr(i + 1));
}

int32_t Finish(ccs_par2_report *r, int32_t status, const std::wstring &message)
{
    if (r) {
        r->status = status;
        size_t cap = sizeof r->message / sizeof r->message[0];
        wcsncpy(r->message, message.c_str(), cap - 1);
        r->message[cap - 1] = 0;
    }
    return status;
}

int32_t FromResult(Result res)
{
    switch (res) {
    case eSuccess: return CCS_PAR2_OK;
    case eRepairPossible: return CCS_PAR2_DAMAGED_REPAIRABLE;
    case eRepairNotPossible: return CCS_PAR2_DAMAGED_NOT_REPAIRABLE;
    case eInsufficientCriticalData: return CCS_PAR2_PARITY_UNUSABLE;
    case eRepairFailed: return CCS_PAR2_REPAIR_FAILED;
    case eFileIOError: return CCS_PAR2_IO_ERROR;
    case eMemoryError: return CCS_PAR2_OUT_OF_MEMORY;
    case eInvalidCommandLineArguments: return CCS_PAR2_BAD_ARGUMENT;
    default: return CCS_PAR2_INTERNAL_ERROR;
    }
}

bool Begin(ccs_par2_report *r)
{
    if (!r || r->size != sizeof(ccs_par2_report)) return false;
    uint32_t size = r->size;
    memset(r, 0, sizeof *r);
    r->size = size; r->library_result = -1;
    return true;
}

const size_t kDefaultMemory = 256u * 1024u * 1024u;

size_t MemoryLimit(uint64_t m)
{
    if (m == 0) return kDefaultMemory;
    if (m < 16u * 1024u * 1024u) m = 16u * 1024u * 1024u;   // libpar2 needs room for at least a chunk per recovery block
    if (sizeof(size_t) < 8 && m > 1024u * 1024u * 1024u) m = 1024u * 1024u * 1024u;
    return (size_t)m;
}

// ---------- verify (shared by verify and repair) ----------

struct VerifyOutcome {
    int32_t status = CCS_PAR2_INTERNAL_ERROR;
    std::wstring message;
};

// one libpar2 verification of data_path (named as recorded) against par2_path; basepath = data_path's folder
VerifyOutcome VerifyOnce(const std::wstring &par2, const std::wstring &data, size_t memory, ProgressBuf &pb, ccs_par2_report *r)
{
    VerifyOutcome o;
    std::ostream out(&pb); out.exceptions(std::ios::badbit);
    ErrorBuf eb; std::ostream err(&eb);
    std::vector<std::string> none;
    Result res;
    {
        CcsRepairer rep(out, err, nlNormal);
        res = rep.Process(memory, Utf8(Folder(data)), Utf8(par2), none, false, false, false, false, 0, true);
        r->library_result = (int32_t)res;
        r->block_size = rep.BlockSize(); r->source_blocks = rep.SourceBlocks();
        r->available_blocks = rep.Available(); r->recovery_blocks = rep.RecoveryAvailable();
        r->damaged_blocks = rep.Missing();
        if (rep.HasMain() && rep.Recoverable() != 1) {
            o.status = CCS_PAR2_BAD_ARGUMENT;
            o.message = L"This parity protects " + std::to_wstring(rep.Recoverable()) + L" files; the Workbench protects one file per parity set.";
            return o;
        }
        bool named = false;
        for (const std::string &t : rep.TargetNames()) if (_wcsicmp(Wide(t).c_str(), data.c_str()) == 0) named = true;
        if (rep.HasMain() && !named) {
            std::vector<std::string> names = rep.TargetNames();
            o.status = CCS_PAR2_BAD_ARGUMENT;
            o.message = L"The parity describes a file named \"" + (names.empty() ? std::wstring(L"?") : Name(Wide(names[0]))) +
                        L"\", not \"" + Name(data) + L"\". Keep the protected file's name as it was when the parity was made.";
            return o;
        }
    }
    o.status = FromResult(res);
    std::wstring blocks = Thousands(r->source_blocks);
    switch (o.status) {
    case CCS_PAR2_OK:
        o.message = L"The file is intact: all " + blocks + L" blocks match the parity (" + Thousands(r->recovery_blocks) + L" recovery blocks intact).";
        break;
    case CCS_PAR2_DAMAGED_REPAIRABLE:
        o.message = Thousands(r->damaged_blocks) + L" of " + blocks + L" blocks are damaged; the " + Thousands(r->recovery_blocks) +
                    L" intact recovery blocks are enough to rebuild them into a new file.";
        break;
    case CCS_PAR2_DAMAGED_NOT_REPAIRABLE:
        o.message = Thousands(r->damaged_blocks) + L" of " + blocks + L" blocks are damaged and only " + Thousands(r->recovery_blocks) +
                    L" intact recovery blocks remain; this parity cannot rebuild the file.";
        break;
    case CCS_PAR2_PARITY_UNUSABLE:
        o.message = L"The parity files could not be read well enough to check the file (" + LastLine(eb.text.empty() ? pb.Log() : eb.text) + L").";
        break;
    default:
        o.message = L"Checking against the parity did not finish (" + LastLine(eb.text.empty() ? pb.Log() : eb.text) + L").";
        break;
    }
    return o;
}

struct Inputs {
    std::wstring par2, base, data;
    uint64_t parity_bytes = 0;
};

// common checks for verify and repair; on success the files are held
int32_t CheckInputs(const wchar_t *par2_path, const wchar_t *data_path, Inputs &in, Holds &holds, ccs_par2_report *r)
{
    in.par2 = FullPath(par2_path); in.data = FullPath(data_path);
    if (in.par2.empty() || !EndsWithI(in.par2, L".par2")) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The parity file must be a full path ending in .par2.");
    if (in.data.empty()) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The protected file's path is empty.");
    in.base = in.par2.substr(0, in.par2.size() - 5);
    if (!IsFile(in.par2)) return Finish(r, CCS_PAR2_PARITY_UNUSABLE, L"The parity file " + Name(in.par2) + L" is missing.");
    if (!IsFile(in.data, &r->source_bytes)) return Finish(r, CCS_PAR2_IO_ERROR, L"The protected file " + Name(in.data) + L" is missing.");
    DWORD e = 0;
    if (!holds.Add(in.data, &e)) return Finish(r, CCS_PAR2_IO_ERROR, L"Could not open " + Name(in.data) + L" for reading only: " + Win32Error(e) + L".");
    for (const std::wstring &f : ParityFiles(in.base)) {
        uint64_t s = 0; IsFile(f, &s); in.parity_bytes += s;
        if (!holds.Add(f, &e)) return Finish(r, CCS_PAR2_IO_ERROR, L"Could not open " + Name(f) + L" for reading only: " + Win32Error(e) + L".");
    }
    r->parity_bytes = in.parity_bytes;
    return CCS_PAR2_OK;
}

void RemoveTree(const std::wstring &folder)
{
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((folder + L"\\*").c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            std::wstring n = fd.cFileName;
            if (n == L"." || n == L"..") continue;
            std::wstring p = folder + L"\\" + n;
            if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) RemoveTree(p);
            else { SetFileAttributesW(p.c_str(), FILE_ATTRIBUTE_NORMAL); DeleteFileW(p.c_str()); }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    RemoveDirectoryW(folder.c_str());
}

}  // namespace

// ---------- the ABI ----------

extern "C" int32_t ccs_par2_create(const wchar_t *data_path, const wchar_t *par2_path, uint32_t redundancy_pct, uint32_t volume_count,
                                   uint64_t memory_limit, ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel,
                                   ccs_par2_report *r)
{
    if (!Begin(r)) return CCS_PAR2_BAD_ARGUMENT;
    BusyGuard busy;
    if (!busy.ok) return Finish(r, CCS_PAR2_BUSY, L"Another parity task is running in this window; try again when it has finished.");
    if (redundancy_pct < 1 || redundancy_pct > 100) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"Redundancy must be between 1% and 100%.");
    if (volume_count == 0) volume_count = 1;
    if (volume_count > 32) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"At most 32 parity volume files.");
    std::wstring data = FullPath(data_path), par2 = FullPath(par2_path);
    if (data.empty() || par2.empty() || !EndsWithI(par2, L".par2"))
        return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The protected file and a parity file name ending in .par2 are both needed, as full paths.");
    std::wstring base = par2.substr(0, par2.size() - 5);
    if (_wcsicmp(Folder(data).c_str(), Folder(par2).c_str()) != 0)
        return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The parity files must be made in the same folder as the file they protect.");
    if (!IsFile(data, &r->source_bytes)) return Finish(r, CCS_PAR2_IO_ERROR, L"The file to protect, " + Name(data) + L", is missing.");
    if (r->source_bytes == 0) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The file to protect is empty.");
    if (!IsFolder(Folder(par2))) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The folder for the parity files does not exist.");
    if (!ParityFiles(base, true).empty() || Exists(par2))
        return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"Parity files named " + Name(base) + L".*par2 already exist there; nothing was changed.");

    // block size and counts as par2cmdline chooses them by default (about 2,000 source blocks; -r<pct>)
    uint64_t total = r->source_bytes;
    uint64_t bs = (total + 1999) / 2000; if (bs < 4) bs = 4; bs = (bs + 3) & ~(uint64_t)3;
    uint64_t minbs = (total + 32767) / 32768; if (minbs < 4) minbs = 4; minbs = (minbs + 3) & ~(uint64_t)3;
    if (bs < minbs) bs = minbs;
    if (bs > 0xFFFFFFFFull) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The file is too large for one parity set.");
    uint32_t sourceblocks = (uint32_t)((total + bs - 1) / bs);
    uint32_t recoveryblocks = (uint32_t)(((uint64_t)sourceblocks * redundancy_pct + 50) / 100);
    if (recoveryblocks == 0) recoveryblocks = 1;
    if (volume_count > recoveryblocks) volume_count = recoveryblocks;
    r->block_size = (uint32_t)bs; r->source_blocks = sourceblocks;

    Holds holds; DWORD e = 0;
    if (!holds.Add(data, &e)) return Finish(r, CCS_PAR2_IO_ERROR, L"Could not open " + Name(data) + L" for reading only: " + Win32Error(e) + L".");

    ProgressBuf pb(kCreate, progress, context, cancel);
    ErrorBuf eb;
    int32_t status = CCS_PAR2_INTERNAL_ERROR; std::wstring message;
    try {
        pb.CheckCancel();
        std::ostream out(&pb); out.exceptions(std::ios::badbit);
        std::ostream err(&eb);
        std::vector<std::string> files{ Utf8(data) };
        Result res;
        {
            CcsCreator creator(out, err, nlNormal);
            res = creator.Process(MemoryLimit(memory_limit), Utf8(Folder(data)), Utf8(base), files, bs, 0, scUniform, volume_count, recoveryblocks);
        }
        r->library_result = (int32_t)res;
        status = FromResult(res);
        if (status != CCS_PAR2_OK) message = L"The parity could not be made (" + LastLine(eb.text.empty() ? pb.Log() : eb.text) + L").";
    } catch (const CancelRequest &) {
        status = CCS_PAR2_CANCELED; message = L"Stopped before the parity was finished; no parity files were kept.";
    } catch (const std::bad_alloc &) {
        status = CCS_PAR2_OUT_OF_MEMORY; message = L"Not enough memory to make the parity.";
    } catch (...) {
        status = CCS_PAR2_INTERNAL_ERROR; message = L"The parity library stopped unexpectedly.";
    }
    if (status != CCS_PAR2_OK) {
        for (const std::wstring &f : ParityFiles(base, true)) DeleteFileW(f.c_str());   // none existed before this call
        return Finish(r, status, message);
    }
    std::vector<std::wstring> made = ParityFiles(base);
    r->recovery_blocks = recoveryblocks;
    r->recovery_files = made.empty() ? 0 : (uint32_t)made.size() - 1;
    for (const std::wstring &f : made) { uint64_t s = 0; IsFile(f, &s); r->parity_bytes += s; }
    pb.SetStage(CCS_PAR2_STAGE_WRITING, 1000);
    return Finish(r, CCS_PAR2_OK, L"Parity made: " + Thousands(recoveryblocks) + L" recovery blocks (" + std::to_wstring(redundancy_pct) +
                                  L"%) for " + Thousands(sourceblocks) + L" blocks of " + Thousands(bs) + L" bytes, in " +
                                  std::to_wstring(r->recovery_files) + L" volume file" + (r->recovery_files == 1 ? L"" : L"s") +
                                  L" and an index; " + Bytes(r->parity_bytes) + L" in all.");
}

extern "C" int32_t ccs_par2_verify(const wchar_t *par2_path, const wchar_t *data_path, uint64_t memory_limit,
                                   ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *r)
{
    if (!Begin(r)) return CCS_PAR2_BAD_ARGUMENT;
    BusyGuard busy;
    if (!busy.ok) return Finish(r, CCS_PAR2_BUSY, L"Another parity task is running in this window; try again when it has finished.");
    Inputs in; Holds holds;
    int32_t s = CheckInputs(par2_path, data_path, in, holds, r);
    if (s != CCS_PAR2_OK) return s;
    ProgressBuf pb(kVerify, progress, context, cancel);
    try {
        pb.CheckCancel();
        VerifyOutcome o = VerifyOnce(in.par2, in.data, MemoryLimit(memory_limit), pb, r);
        r->parity_bytes = in.parity_bytes;
        return Finish(r, o.status, o.message);
    } catch (const CancelRequest &) {
        return Finish(r, CCS_PAR2_CANCELED, L"Stopped before the check was finished.");
    } catch (const std::bad_alloc &) {
        return Finish(r, CCS_PAR2_OUT_OF_MEMORY, L"Not enough memory to check the file against its parity.");
    } catch (...) {
        return Finish(r, CCS_PAR2_INTERNAL_ERROR, L"The parity library stopped unexpectedly.");
    }
}

extern "C" int32_t ccs_par2_repair(const wchar_t *par2_path, const wchar_t *data_path, const wchar_t *output_path, uint64_t memory_limit,
                                   ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *r)
{
    if (!Begin(r)) return CCS_PAR2_BAD_ARGUMENT;
    BusyGuard busy;
    if (!busy.ok) return Finish(r, CCS_PAR2_BUSY, L"Another parity task is running in this window; try again when it has finished.");
    std::wstring output = FullPath(output_path);
    if (output.empty()) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"A full path for the rebuilt file is needed.");
    if (Exists(output)) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The rebuilt file " + Name(output) + L" already exists; choose a new name. Nothing was changed.");
    if (!IsFolder(Folder(output))) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The folder for the rebuilt file does not exist.");
    Inputs in; Holds holds;
    int32_t s = CheckInputs(par2_path, data_path, in, holds, r);
    if (s != CCS_PAR2_OK) return s;
    if (_wcsicmp(output.c_str(), in.data.c_str()) == 0) return Finish(r, CCS_PAR2_BAD_ARGUMENT, L"The rebuilt file must be a new file.");

    ProgressBuf pb(kRepair, progress, context, cancel);
    size_t memory = MemoryLimit(memory_limit);
    std::wstring staging;
    try {
        pb.CheckCancel();
        // 1. what is wrong (reads only)
        VerifyOutcome first = VerifyOnce(in.par2, in.data, memory, pb, r);
        if (first.status == CCS_PAR2_OK) return Finish(r, CCS_PAR2_NOT_NEEDED, L"The file is already intact; no rebuilt copy was needed or written.");
        if (first.status != CCS_PAR2_DAMAGED_REPAIRABLE) return Finish(r, first.status, first.message);
        uint32_t damaged = r->damaged_blocks, sourceblocks = r->source_blocks;

        // 2. rebuild in a new private folder beside the output; the original is only an extra file to read blocks from
        wchar_t suffix[64];
        swprintf(suffix, 64, L".ccs-rebuild-%lu-%llu", (unsigned long)GetCurrentProcessId(), (unsigned long long)GetTickCount64());
        staging = Folder(output) + L"." + Name(output) + suffix;
        if (!CreateDirectoryW(staging.c_str(), NULL))
            return Finish(r, CCS_PAR2_IO_ERROR, L"Could not make a working folder beside the rebuilt file: " + Win32Error(GetLastError()) + L".");
        SetFileAttributesW(staging.c_str(), FILE_ATTRIBUTE_HIDDEN);
        std::wstring rebuilt = staging + L"\\" + Name(in.data);
        Result res;
        ErrorBuf eb;
        {
            std::ostream out(&pb); out.exceptions(std::ios::badbit);
            std::ostream err(&eb);
            std::vector<std::string> extra{ Utf8(in.data) };
            CcsRepairer rep(out, err, nlNormal);
            res = rep.Process(memory, Utf8(staging + L"\\"), Utf8(in.par2), extra, true, false, false, false, 0, true);
        }
        r->library_result = (int32_t)res;
        if (res != eSuccess || !IsFile(rebuilt)) {
            RemoveTree(staging);
            int32_t st = res == eSuccess ? CCS_PAR2_REPAIR_FAILED : FromResult(res);
            if (st == CCS_PAR2_OK) st = CCS_PAR2_REPAIR_FAILED;
            return Finish(r, st, L"The file could not be rebuilt (" + LastLine(eb.text.empty() ? pb.Log() : eb.text) + L"); nothing was kept.");
        }

        // 3. an independent check of the rebuilt file against the parity, before it gets its name
        pb.checking_output = true;
        ccs_par2_report check; memset(&check, 0, sizeof check); check.size = sizeof check;
        VerifyOutcome again = VerifyOnce(in.par2, rebuilt, memory, pb, &check);
        if (again.status != CCS_PAR2_OK) {
            RemoveTree(staging);
            return Finish(r, CCS_PAR2_REPAIR_FAILED, L"The rebuilt file did not check out against the parity (" + again.message + L"); nothing was kept.");
        }
        // 4. give it the requested name (same folder, so a rename; never replaces an existing file)
        if (!MoveFileExW(rebuilt.c_str(), output.c_str(), MOVEFILE_WRITE_THROUGH)) {
            DWORD err = GetLastError();
            RemoveTree(staging);
            return Finish(r, CCS_PAR2_IO_ERROR, L"The rebuilt file could not be named " + Name(output) + L": " + Win32Error(err) + L"; nothing was kept.");
        }
        RemoveTree(staging);
        r->damaged_blocks = damaged; r->source_blocks = sourceblocks;
        return Finish(r, CCS_PAR2_OK, L"Rebuilt " + Thousands(damaged) + L" damaged block" + (damaged == 1 ? L"" : L"s") + L" of " +
                                      Thousands(sourceblocks) + L" into the new file " + Name(output) +
                                      L", which matches the parity. The damaged original was not changed.");
    } catch (const CancelRequest &) {
        if (!staging.empty()) RemoveTree(staging);
        return Finish(r, CCS_PAR2_CANCELED, L"Stopped before the rebuild was finished; nothing was kept and the original was not changed.");
    } catch (const std::bad_alloc &) {
        if (!staging.empty()) RemoveTree(staging);
        return Finish(r, CCS_PAR2_OUT_OF_MEMORY, L"Not enough memory to rebuild the file.");
    } catch (...) {
        if (!staging.empty()) RemoveTree(staging);
        return Finish(r, CCS_PAR2_INTERNAL_ERROR, L"The parity library stopped unexpectedly; nothing was kept.");
    }
}

static const ccs_host_abi g_abi = {
    CCS_HOST_ABI_MAGIC, CCS_HOST_ABI_VERSION, (uint32_t)sizeof(ccs_host_abi), (uint32_t)sizeof(ccs_par2_report), 0,
    CCS_HOST_VERSION_W, nullptr, L"par2cmdline " CCS_PAR2_VERSION_W, ccs_par2_create, ccs_par2_verify, ccs_par2_repair,
#ifdef CCS_WITH_HOME   /* the single-file host links the native HOME renderer (build_host.sh) */
    CCS_HOME_CHECK_ABI, 0, ccs_home_render_check, ccs_home_design_boot, ccs_home_design_screen,
#else
    0, 0, nullptr, nullptr, nullptr,
#endif
};

extern "C" __declspec(dllexport) const ccs_host_abi *ccs_host_get_abi(void)
{
    static ccs_host_abi abi = g_abi;
    abi.host_arch = ccs_host_arch();
    return &abi;
}

static_assert(sizeof(ccs_par2_report) == 56 + 2048, "the managed side's NativeReport layout");
static_assert(__builtin_offsetof(ccs_par2_report, source_bytes) == 40, "u64 fields after ten u32 fields");
static_assert(__builtin_offsetof(ccs_host_abi, host_version) == 24, "pointer fields after the header");
static_assert(__builtin_offsetof(ccs_host_abi, home_check_abi) == 24 + 6 * sizeof(void *), "version 2 fields after par2_repair");
static_assert(__builtin_offsetof(ccs_host_abi, home_render_check) == 32 + 6 * sizeof(void *), "the render check after the two u32 fields");
static_assert(__builtin_offsetof(ccs_host_abi, home_design_screen) == 32 + 8 * sizeof(void *), "version 3: the screen after the boot");
