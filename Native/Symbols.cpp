// GPL-3.0. Symbol preparation runs out of process, never inside Explorer.
#define UNICODE
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dbghelp.h>
#include <fstream>
#include <filesystem>
#include <iostream>
#include <vector>
#include <string>

static std::ofstream output;
static BOOL CALLBACK Collect(PSYMBOL_INFOW symbol, ULONG, void*) {
    std::wstring name(symbol->Name, symbol->NameLen);
    wchar_t undecorated[8192]{};
    if (UnDecorateSymbolNameW(name.c_str(), undecorated, 8192, UNDNAME_COMPLETE)) name = undecorated;
    int length = WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), nullptr, 0, nullptr, nullptr);
    std::string utf8(length, '\0');
    WideCharToMultiByte(CP_UTF8, 0, name.data(), static_cast<int>(name.size()), utf8.data(), length, nullptr, nullptr);
    output << std::hex << (symbol->Address - symbol->ModBase) << '\t' << utf8 << '\n';
    return TRUE;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 4) { std::cerr << "Usage: TrayBridge.Symbols module.dll pdb-directory output.symmap\n"; return 2; }
    HANDLE process = GetCurrentProcess();
    SymSetOptions(SYMOPT_DEFERRED_LOADS | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_EXACT_SYMBOLS);
    if (!SymInitializeW(process, argv[2], FALSE)) { std::cerr << "SymInitialize: " << GetLastError(); return 3; }
    DWORD64 base = SymLoadModuleExW(process, nullptr, argv[1], nullptr, 0x10000000, 0, nullptr, 0);
    if (!base) { std::cerr << "SymLoadModuleEx: " << GetLastError(); SymCleanup(process); return 4; }
    IMAGEHLP_MODULEW64 info{}; info.SizeOfStruct = sizeof(info);
    if (!SymGetModuleInfoW64(process, base, &info) || info.SymType == SymNone) {
        std::cerr << "Matching PDB unavailable: " << GetLastError(); SymCleanup(process); return 5;
    }
    output.open(std::filesystem::path(argv[3]), std::ios::trunc);
    std::ifstream image(std::filesystem::path(argv[1]), std::ios::binary);
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS64 nt{};
    image.read(reinterpret_cast<char*>(&dos), sizeof(dos)); image.seekg(dos.e_lfanew); image.read(reinterpret_cast<char*>(&nt), sizeof(nt));
    if (!image || dos.e_magic != IMAGE_DOS_SIGNATURE || nt.Signature != IMAGE_NT_SIGNATURE) { std::cerr << "Invalid Windows PE image"; return 7; }
    output << "TRAYBRIDGE1\t" << std::hex << nt.FileHeader.TimeDateStamp << '\t' << nt.OptionalHeader.SizeOfImage << '\n';
    BOOL ok = SymEnumSymbolsW(process, base, nullptr, Collect, nullptr);
    // Optional diagnostic for developing version-checked private bindings.
    if (GetEnvironmentVariableW(L"TRAYBRIDGE_TYPES", nullptr, 0)) {
        std::vector<BYTE> buffer(sizeof(SYMBOL_INFOW) + 512 * sizeof(wchar_t));
        auto type = reinterpret_cast<PSYMBOL_INFOW>(buffer.data()); type->SizeOfStruct = sizeof(SYMBOL_INFOW); type->MaxNameLen = 512;
        if (SymGetTypeFromNameW(process, base, L"NotificationAreaIconIdentity", type)) {
            ULONG count{}; ULONG64 length{};
            SymGetTypeInfo(process, base, type->TypeIndex, TI_GET_LENGTH, &length);
            SymGetTypeInfo(process, base, type->TypeIndex, TI_GET_CHILDRENCOUNT, &count);
            std::cout << "identity length " << length << " children " << count << "\n";
            std::vector<BYTE> storage(sizeof(TI_FINDCHILDREN_PARAMS) + count * sizeof(ULONG));
            auto children = reinterpret_cast<TI_FINDCHILDREN_PARAMS*>(storage.data()); children->Count = count; children->Start = 0;
            if (SymGetTypeInfo(process, base, type->TypeIndex, TI_FINDCHILDREN, children)) for (ULONG i = 0; i < count; ++i) {
                wchar_t* name{}; ULONG offset{};
                SymGetTypeInfo(process, base, children->ChildId[i], TI_GET_SYMNAME, &name);
                SymGetTypeInfo(process, base, children->ChildId[i], TI_GET_OFFSET, &offset);
                if (name) { std::wcout << name << L" offset " << offset << L"\n"; LocalFree(name); }
            }
        } else std::cerr << "identity type unavailable " << GetLastError() << "\n";
    }
    output.close(); SymCleanup(process);
    if (!ok) { std::cerr << "SymEnumSymbols failed"; return 6; }
    return 0;
}
