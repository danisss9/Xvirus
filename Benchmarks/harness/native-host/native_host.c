// Xvirus Native SDK (AOT XvirusSDK.dll) benchmark host.
// Build: gcc -O2 -o native_host.exe native_host.c -lpsapi
// Usage: native_host.exe <sdkFolder> <planFile> <outFile>
// Plan lines: baseline | load | unload | update | scan <warm> <reps> <path> | folder <path>
#include <windows.h>
#include <psapi.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

typedef struct { bool Sucess; wchar_t *Result; wchar_t *Error; } ActionResult;
typedef struct { bool Sucess; wchar_t *Error; bool IsMalware; wchar_t *Name; double MalwareScore; wchar_t *Path; } ScanResult;

typedef ActionResult (*LoadFn)(bool);
typedef ActionResult (*UnloadFn)(void);
typedef ScanResult (*ScanFn)(const wchar_t *);
typedef ScanResult *(*ScanFolderFn)(const wchar_t *);
typedef ActionResult (*UpdateFn)(bool);
typedef wchar_t *(*BaseFolderFn)(const wchar_t *);

static LARGE_INTEGER freq;
static FILE *out;

static double now_ms(void) { LARGE_INTEGER c; QueryPerformanceCounter(&c); return (double)c.QuadPart * 1000.0 / (double)freq.QuadPart; }

static double cpu_ms(void) {
    FILETIME c, e, k, u; GetProcessTimes(GetCurrentProcess(), &c, &e, &k, &u);
    ULARGE_INTEGER kk, uu; kk.LowPart = k.dwLowDateTime; kk.HighPart = k.dwHighDateTime; uu.LowPart = u.dwLowDateTime; uu.HighPart = u.dwHighDateTime;
    return (double)(kk.QuadPart + uu.QuadPart) / 10000.0;
}

static void mem(double *ws, double *peak, double *priv) {
    PROCESS_MEMORY_COUNTERS_EX pmc; pmc.cb = sizeof(pmc);
    GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS *)&pmc, sizeof(pmc));
    *ws = pmc.WorkingSetSize / 1048576.0; *peak = pmc.PeakWorkingSetSize / 1048576.0; *priv = pmc.PrivateUsage / 1048576.0;
}

static void json_str(const char *s) {
    fputc('"', out);
    for (; *s; s++) { if (*s == '"' || *s == '\\') fputc('\\', out); if ((unsigned char)*s >= 0x20) fputc(*s, out); }
    fputc('"', out);
}

static void wtoutf8(const wchar_t *w, char *buf, int n) {
    if (!w) { buf[0] = 0; return; }
    WideCharToMultiByte(CP_UTF8, 0, w, -1, buf, n, NULL, NULL);
}

static void emit(const char *op, const char *path, double *times, int nt, double cpu, const char *result, int files) {
    double ws, peak, priv; mem(&ws, &peak, &priv);
    fprintf(out, "{\"op\":\"%s\",\"path\":", op); json_str(path);
    fprintf(out, ",\"times_ms\":[");
    for (int i = 0; i < nt; i++) fprintf(out, "%s%.4f", i ? "," : "", times[i]);
    fprintf(out, "],\"cpu_ms\":%.3f,\"ws_mb\":%.3f,\"peak_ws_mb\":%.3f,\"private_mb\":%.3f,\"result\":", cpu, ws, peak, priv);
    json_str(result);
    fprintf(out, ",\"files\":%d,\"threads\":1}\n", files);
    fflush(out);
}

int main(int argc, char **argv) {
    if (argc < 4) { fprintf(stderr, "usage: native_host <sdkFolder> <plan> <out>\n"); return 2; }
    QueryPerformanceFrequency(&freq);
    out = fopen(argv[3], "w");

    wchar_t dir[MAX_PATH], dll[MAX_PATH];
    MultiByteToWideChar(CP_UTF8, 0, argv[1], -1, dir, MAX_PATH);
    SetDllDirectoryW(dir);
    swprintf(dll, MAX_PATH, L"%ls\\XvirusSDK.dll", dir);

    // Baseline is captured before the SDK DLL is mapped into the process.
    FILE *plan = fopen(argv[2], "r");
    char line[4096], res[8192];
    HMODULE lib = NULL;
    LoadFn load = NULL; UnloadFn unload = NULL; ScanFn scan = NULL; ScanFolderFn scanFolder = NULL; UpdateFn update = NULL; BaseFolderFn baseFolder = NULL;

    while (fgets(line, sizeof(line), plan)) {
        line[strcspn(line, "\r\n")] = 0;
        if (!line[0] || line[0] == '#') continue;
        double c0 = cpu_ms(), t0, t;

        if (strcmp(line, "baseline") == 0) { emit("baseline", "", NULL, 0, 0, "", 0); continue; }

        if (!lib) {
            lib = LoadLibraryW(dll);
            if (!lib) { fprintf(stderr, "LoadLibrary failed %lu\n", GetLastError()); return 1; }
            load = (LoadFn)GetProcAddress(lib, "load"); unload = (UnloadFn)GetProcAddress(lib, "unload");
            scan = (ScanFn)GetProcAddress(lib, "scan"); scanFolder = (ScanFolderFn)GetProcAddress(lib, "scanFolder");
            update = (UpdateFn)GetProcAddress(lib, "checkUpdates"); baseFolder = (BaseFolderFn)GetProcAddress(lib, "baseFolder");
            LocalFree(baseFolder(dir));
        }

        if (strcmp(line, "load") == 0) {
            t0 = now_ms(); ActionResult r = load(false); t = now_ms() - t0;
            wtoutf8(r.Sucess ? r.Result : r.Error, res, sizeof(res));
            emit("load", "", &t, 1, cpu_ms() - c0, res, 0);
        } else if (strcmp(line, "unload") == 0) {
            t0 = now_ms(); ActionResult r = unload(); t = now_ms() - t0;
            emit("unload", "", &t, 1, cpu_ms() - c0, "ok", 0); (void)r;
        } else if (strcmp(line, "update") == 0) {
            t0 = now_ms(); ActionResult r = update(false); t = now_ms() - t0;
            wtoutf8(r.Sucess ? r.Result : r.Error, res, sizeof(res));
            emit("update", "", &t, 1, cpu_ms() - c0, res, 0);
        } else if (strncmp(line, "scan ", 5) == 0) {
            int warm, reps, off = 0;
            sscanf(line + 5, "%d %d %n", &warm, &reps, &off);
            const char *path = line + 5 + off;
            wchar_t wpath[MAX_PATH * 2]; MultiByteToWideChar(CP_UTF8, 0, path, -1, wpath, MAX_PATH * 2);
            double warmT[16], times[256]; char name[512] = "";
            ScanResult r;
            for (int i = 0; i < warm; i++) {
                t0 = now_ms(); r = scan(wpath); warmT[i] = now_ms() - t0;
                wtoutf8(r.Sucess ? r.Name : r.Error, name, sizeof(name));
                LocalFree(r.Sucess ? r.Name : r.Error); LocalFree(r.Path);
            }
            double c1 = cpu_ms();
            for (int i = 0; i < reps; i++) {
                t0 = now_ms(); r = scan(wpath); times[i] = now_ms() - t0;
                wtoutf8(r.Sucess ? r.Name : r.Error, name, sizeof(name));
                LocalFree(r.Sucess ? r.Name : r.Error); LocalFree(r.Path);
            }
            double cpu = cpu_ms() - c1;
            int n = snprintf(res, sizeof(res), "%s|warm=", name);
            for (int i = 0; i < warm; i++) n += snprintf(res + n, sizeof(res) - n, "%s%.3f", i ? "," : "", warmT[i]);
            emit("scan", path, times, reps, cpu, res, 0);
        } else if (strncmp(line, "folder ", 7) == 0) {
            const char *path = line + 7;
            wchar_t wpath[MAX_PATH * 2]; MultiByteToWideChar(CP_UTF8, 0, path, -1, wpath, MAX_PATH * 2);
            // scanFolder returns one ScanResult per file followed by an all-zero terminator.
            int files = 0, malware = 0;
            t0 = now_ms();
            ScanResult *arr = scanFolder(wpath);
            for (ScanResult *r = arr; arr && r->Path != NULL; r++) {
                files++; if (r->Sucess && r->IsMalware) malware++;
                LocalFree(r->Sucess ? r->Name : r->Error); LocalFree(r->Path);
            }
            LocalFree(arr);
            t = now_ms() - t0;
            snprintf(res, sizeof(res), "malware=%d", malware);
            emit("folder", path, &t, 1, cpu_ms() - c0, res, files);
        }
    }
    fclose(out);
    return 0;
}
