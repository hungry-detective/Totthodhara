// Portable launcher stub: this ships as deploy/Totthodhara.exe (the file
// friends double-click). The real app + all Qt DLLs/plugins/QML live in
// deploy/library/, user data in deploy/data/. Windows only loads DLLs
// from beside the exe (or PATH), so the stub sets TOTTHODHARA_DATA_DIR to
// <root>/data and starts library/Totthodhara.exe with forwarded args.
// WIN32 subsystem: no console window, ever.
#include <windows.h>

#include <cstdio>
#include <string>

int WINAPI WinMain(HINSTANCE, HINSTANCE, LPSTR, int)
{
    // No console in this subsystem: every failure path below reports
    // through MessageBoxW, never a silent nothing-happens double-click.
    auto fail = [](const std::wstring &what, DWORD err) -> int {
        wchar_t buf[64];
        swprintf(buf, 64, L" (error %lu)", (unsigned long)err);
        MessageBoxW(nullptr, (what + buf).c_str(), L"Totthodhara", MB_OK | MB_ICONERROR);
        return 1;
    };
    wchar_t self[MAX_PATH];
    const DWORD len = GetModuleFileNameW(nullptr, self, MAX_PATH);
    // len==0 failed; len>=MAX_PATH truncated (root would point elsewhere).
    if (len == 0 || len >= MAX_PATH)
        return fail(L"Cannot locate the app folder.", GetLastError());
    std::wstring root(self, len);
    const size_t bs = root.find_last_of(L"\\/");
    root = (bs == std::wstring::npos) ? L"." : root.substr(0, bs);

    // Data stays at the portable root even though the real exe is one
    // level down (AppPaths honors this over <exe>/data). The root itself
    // travels along too, so the in-app updater can find its layout.
    SetEnvironmentVariableW(L"TOTTHODHARA_DATA_DIR", (root + L"\\data").c_str());
    SetEnvironmentVariableW(L"TOTTHODHARA_ROOT", root.c_str());

    // Forward our own args (e.g. --settings) to the real app. Args are
    // re-wrapped in quotes (spaces in paths); exotic embedded quotes in
    // user args are passed through as-is (only --settings is used).
    std::wstring cmd = L"\"" + root + L"\\library\\Totthodhara.exe\"";
    int argc = 0;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv)
        return fail(L"Cannot read the command line.", GetLastError());
    for (int i = 1; i < argc; ++i) {
        cmd += L" \"";
        cmd += argv[i];
        cmd += L'"';
    }
    LocalFree(argv);

    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(nullptr, cmd.data(), nullptr, nullptr, FALSE, 0,
                        nullptr, root.c_str(), &si, &pi))
        return fail(L"Cannot start library\\Totthodhara.exe.", GetLastError());
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
