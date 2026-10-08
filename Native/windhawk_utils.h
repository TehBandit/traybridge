// TrayBridge's focused compatibility host for the upstream tray implementation.
// GPL-3.0; hook lifecycle adapted from Windhawk's engine/mod.cpp.
#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <string>
#include <vector>
#include <initializer_list>
#include <type_traits>
#include <cstdint>
#define WH_MOD_ID L"traybridge"

void Wh_Log(PCWSTR format, ...);
BOOL Wh_SetFunctionHook(void* target, void* hook, void** original);
BOOL Wh_ApplyHookOperations();
int Wh_GetIntSetting(PCWSTR name);
std::wstring BridgeStringSetting(PCWSTR name);

namespace WindhawkUtils {
struct SYMBOL_HOOK {
    std::vector<std::wstring> symbols;
    void** original;
    void* hook;
    bool optional;
    template<class T> SYMBOL_HOOK(std::initializer_list<PCWSTR> names, T* address, T replacement = nullptr, bool isOptional = false)
        : original(reinterpret_cast<void**>(address)), hook(reinterpret_cast<void*>(replacement)), optional(isOptional) {
        for (auto name : names) symbols.emplace_back(name);
    }
};
template<class T, class U> bool SetFunctionHook(T target, U replacement, T* original) {
    return Wh_SetFunctionHook(reinterpret_cast<void*>(target), reinterpret_cast<void*>(replacement), reinterpret_cast<void**>(original));
}
class StringSetting {
    std::wstring value;
    explicit StringSetting(std::wstring text) : value(std::move(text)) {}
public:
    static StringSetting make(PCWSTR name) { return StringSetting{BridgeStringSetting(name)}; }
    PCWSTR get() const { return value.c_str(); }
};
using SubclassProc = LRESULT (*)(HWND, UINT, WPARAM, LPARAM, DWORD_PTR);
bool SetWindowSubclassFromAnyThread(HWND window, SubclassProc proc, DWORD_PTR data);
bool RemoveWindowSubclassFromAnyThread(HWND window, SubclassProc proc);
}
bool HookSymbols(HMODULE module, const WindhawkUtils::SYMBOL_HOOK* hooks, size_t count);
