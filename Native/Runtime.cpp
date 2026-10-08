// GPL-3.0. A source-built, process-scoped adaptation of the Windhawk hook API.
#include "windhawk_utils.h"
#include "vendor/MinHook/include/MinHook.h"
#include <winrt/Windows.Data.Json.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <map>
#include <mutex>
#include <cstdarg>
#include <algorithm>
#include <shlobj.h>

std::wstring bridgeDirectory;
std::wstring bridgeStatus;
std::mutex bridgeLogLock;
bool bridgeVerbose = false;
void Wh_Log(PCWSTR format, ...) {
    if (!bridgeVerbose || bridgeDirectory.empty()) return;
    wchar_t buffer[4096]{};
    va_list args; va_start(args, format); _vsnwprintf_s(buffer, _TRUNCATE, format, args); va_end(args);
    std::lock_guard<std::mutex> guard(bridgeLogLock);
    std::wofstream log(std::filesystem::path(bridgeDirectory) / (L"native-" + std::to_wstring(GetCurrentProcessId()) + L".log"), std::ios::app);
    log << buffer << L'\n';
}
// Adapted from Windhawk LoadedMod::SetFunctionHook: create and queue, then apply in a batch.
BOOL Wh_SetFunctionHook(void* target, void* hook, void** original) {
    if (!target || !hook) return FALSE;
    static std::map<void*, void*> originals;
    static std::mutex hookLock;
    std::lock_guard<std::mutex> guard(hookLock);
    auto found = originals.find(target);
    if (found != originals.end()) {
        if (original) *original = found->second;
        return MH_QueueEnableHook(target) == MH_OK;
    }
    void* trampoline = nullptr;
    MH_STATUS status = MH_CreateHook(target, hook, &trampoline);
    if (status != MH_OK && status != MH_ERROR_ALREADY_CREATED) { bridgeStatus = L"Function hook failed: " + std::to_wstring(status); return FALSE; }
    if (status == MH_OK) { originals[target] = trampoline; if (original) *original = trampoline; }
    return MH_QueueEnableHook(target) == MH_OK;
}
BOOL Wh_ApplyHookOperations() { return MH_ApplyQueued() == MH_OK; }
int Wh_GetIntSetting(PCWSTR name) { return bridgeVerbose && wcscmp(name, L"enableTreeDump") == 0; }
std::wstring BridgeStringSetting(PCWSTR name) {
    if (wcscmp(name, L"components") == 0) return L"all";
    if (wcscmp(name, L"componentsScope") == 0) return L"allTaskbars";
    if (wcscmp(name, L"monitorMode") == 0) return L"all";
    return L"";
}
static std::wstring Normalize(std::wstring value) {
    for (auto token : {L"__ptr64", L"class ", L"struct ", L"enum "}) {
        for (size_t pos = value.find(token); pos != std::wstring::npos; pos = value.find(token)) value.erase(pos, wcslen(token));
    }
    value.erase(std::remove_if(value.begin(), value.end(), [](wchar_t c) { return iswspace(c); }), value.end());
    return value;
}
static std::map<std::wstring, uintptr_t> ReadSymbols(HMODULE module) {
    wchar_t path[32768]{}; GetModuleFileNameW(module, path, 32768);
    auto filename = std::filesystem::path(path).filename();
    std::ifstream file(std::filesystem::path(bridgeDirectory) / L"symbols" / (filename.wstring() + L".symmap"));
    std::string line; std::map<std::wstring, uintptr_t> result;
    if (!std::getline(file, line)) { bridgeStatus = L"Prepare symbols for " + filename.wstring(); return result; }
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(module);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<BYTE*>(module) + dos->e_lfanew);
    std::istringstream header(line); std::string marker; DWORD stamp{}, size{};
    header >> marker >> std::hex >> stamp >> size;
    if (marker != "TRAYBRIDGE1" || stamp != nt->FileHeader.TimeDateStamp || size != nt->OptionalHeader.SizeOfImage) {
        bridgeStatus = L"Windows component changed; prepare matching symbols for " + filename.wstring(); return result;
    }
    while (std::getline(file, line)) {
        auto tab = line.find('\t'); if (tab == std::string::npos) continue;
        uintptr_t rva{}; std::istringstream(line.substr(0, tab)) >> std::hex >> rva;
        auto name = line.substr(tab + 1);
        int length = MultiByteToWideChar(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), nullptr, 0);
        std::wstring wide(length, '\0'); MultiByteToWideChar(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), wide.data(), length);
        if (rva < size) result.emplace(Normalize(wide), rva);
    }
    return result;
}
static const std::map<std::wstring, uintptr_t>& CachedSymbols(HMODULE module) {
    static std::map<HMODULE, std::map<std::wstring, uintptr_t>> cache;
    static std::mutex cacheLock;
    std::lock_guard<std::mutex> guard(cacheLock);
    auto found = cache.find(module);
    if (found == cache.end()) found = cache.emplace(module, ReadSymbols(module)).first;
    return found->second;
}
bool HookSymbols(HMODULE module, const WindhawkUtils::SYMBOL_HOOK* hooks, size_t count) {
    const auto& symbols = CachedSymbols(module);
    // Resolve every mandatory address before creating any hooks.
    std::vector<void*> addresses(count);
    for (size_t i = 0; i < count; ++i) {
        for (const auto& name : hooks[i].symbols) {
            auto found = symbols.find(Normalize(name));
            if (found != symbols.end()) { addresses[i] = reinterpret_cast<BYTE*>(module) + found->second; break; }
        }
        if (!addresses[i] && !hooks[i].optional) { bridgeStatus = L"Required Windows symbol unavailable: " + hooks[i].symbols.front(); return false; }
    }
    for (size_t i = 0; i < count; ++i) {
        if (!addresses[i]) continue;
        if (hooks[i].hook) { if (!Wh_SetFunctionHook(addresses[i], hooks[i].hook, hooks[i].original)) return false; }
        else if (hooks[i].original) *hooks[i].original = addresses[i];
    }
    return true;
}
namespace WindhawkUtils {
// Subclass installation is performed on the owning UI thread by the upstream mod.
static LRESULT CALLBACK SubclassThunk(HWND window, UINT message, WPARAM wparam, LPARAM lparam, UINT_PTR id, DWORD_PTR data) {
    return reinterpret_cast<SubclassProc>(id)(window, message, wparam, lparam, data);
}
bool SetWindowSubclassFromAnyThread(HWND window, SubclassProc proc, DWORD_PTR data) {
    if (GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId()) return false;
    return SetWindowSubclass(window, SubclassThunk, reinterpret_cast<UINT_PTR>(proc), data);
}
bool RemoveWindowSubclassFromAnyThread(HWND window, SubclassProc proc) {
    if (GetWindowThreadProcessId(window, nullptr) != GetCurrentThreadId()) return false;
    return RemoveWindowSubclass(window, SubclassThunk, reinterpret_cast<UINT_PTR>(proc));
}
}
