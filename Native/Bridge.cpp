// GPL-3.0. Standalone TrayBridge host; no Windhawk executable or installer.
#include "Runtime.cpp"
#include "Tray.cpp"
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <thread>

using namespace winrt::Windows::Data::Json;
std::atomic<bool> bridgeStop{false};
std::atomic<bool> bridgeRunning{false};

// Resolve a private WinRT getter using this exact binary's PDB, then verify
// that QueryInterface's vtable contains that getter before invoking it.
// No guessed object offsets or guessed interface method order.
static HRESULT ModelGetter(winrt::Windows::Foundation::IInspectable const& object, PCWSTR interfaceName, PCWSTR getter, void* result) {
    if (!object) return E_NOINTERFACE;
    HMODULE module = GetSystemTrayViewModule();
    if (!module) return E_NOINTERFACE;
    auto runtimeClass = std::wstring(winrt::get_class_name(object));
    auto dot = runtimeClass.rfind(L'.');
    auto implementation = runtimeClass.substr(dot == std::wstring::npos ? 0 : dot + 1);
    const auto& symbols = CachedSymbols(module);
    std::wstring iidName = L"winrt::impl::guid_v<winrt::SystemTray::" + std::wstring(interfaceName) + L">";
    std::wstring getterName = L"public:virtualint__cdeclwinrt::impl::produce<winrt::SystemTray::implementation::" + implementation + L",winrt::SystemTray::" + interfaceName + L">::" + getter + L"(";
    const GUID* iid = nullptr; void* function = nullptr;
    static std::map<std::wstring, std::pair<const GUID*, void*>> accessors;
    static std::mutex accessorLock;
    auto cacheKey = std::to_wstring(reinterpret_cast<uintptr_t>(module)) + L":" + implementation + L":" + interfaceName + L":" + getter;
    std::unique_lock<std::mutex> guard(accessorLock);
    auto cached = accessors.find(cacheKey);
    if (cached != accessors.end()) { iid = cached->second.first; function = cached->second.second; }
    else { for (const auto& entry : symbols) {
        if (entry.first.find(iidName) != std::wstring::npos && entry.first.find(L"guidconst") != std::wstring::npos) iid = reinterpret_cast<const GUID*>(reinterpret_cast<BYTE*>(module) + entry.second);
        if (entry.first.compare(0, getterName.size(), getterName) == 0) function = reinterpret_cast<BYTE*>(module) + entry.second;
    }
    accessors[cacheKey] = {iid, function}; }
    guard.unlock();
    if (!iid || !function) return E_NOINTERFACE;
    void* abi = nullptr;
    auto unknown = reinterpret_cast<IUnknown*>(winrt::get_abi(object));
    if (FAILED(unknown->QueryInterface(*iid, &abi)) || !abi) return E_NOINTERFACE;
    auto table = *reinterpret_cast<void***>(abi);
    MEMORY_BASIC_INFORMATION region{};
    bool found = false;
    if (VirtualQuery(table, &region, sizeof(region)) && region.State == MEM_COMMIT && !(region.Protect & (PAGE_NOACCESS | PAGE_GUARD))) {
        auto available = (reinterpret_cast<uintptr_t>(region.BaseAddress) + region.RegionSize - reinterpret_cast<uintptr_t>(table)) / sizeof(void*);
        for (size_t i = 6; i < std::min<size_t>(available, 64); ++i) if (table[i] == function) { found = true; break; }
    }
    HRESULT code = E_NOINTERFACE;
    if (found) code = reinterpret_cast<HRESULT(WINAPI*)(void*, void*)>(function)(abi, result);
    reinterpret_cast<IUnknown*>(abi)->Release();
    return code;
}
static winrt::Windows::Foundation::IInspectable ObjectGetter(winrt::Windows::Foundation::IInspectable const& object, PCWSTR iface, PCWSTR getter) {
    void* value = nullptr;
    if (FAILED(ModelGetter(object, iface, getter, &value)) || !value) return nullptr;
    return {value, winrt::take_ownership_from_abi};
}
static winrt::Windows::Foundation::IInspectable UdkIcon(winrt::Windows::Foundation::IInspectable const& model) {
    auto configuration = ObjectGetter(model, L"IIconViewModel", L"get_Configuration");
    auto data = ObjectGetter(configuration, L"IIconConfiguration", L"get_DataModel");
    return ObjectGetter(data, L"INotificationAreaIconsDataModel", L"get_UDKObject");
}
// ABI member order and IID from the WindowsUdk.winmd installed with Windows.
// Adapted from Ris Peng's GPL-3.0-only system-tray-folders mod.
static std::wstring AppName(winrt::Windows::Foundation::IInspectable const& model) {
    auto icon = UdkIcon(model); if (!icon) return L"";
    constexpr GUID iid{0xD76CAD80, 0x3103, 0x54F4, {0x9A,0xBE,0x5F,0x04,0x7C,0xBF,0xE8,0x5C}};
    winrt::com_ptr<IUnknown> icon6;
    reinterpret_cast<IUnknown*>(winrt::get_abi(icon))->QueryInterface(iid, icon6.put_void());
    if (!icon6) return L"";
    winrt::hstring text;
    auto function = (*reinterpret_cast<void***>(icon6.get()))[6];
    if (FAILED(reinterpret_cast<HRESULT(WINAPI*)(void*, void*)>(function)(icon6.get(), winrt::put_abi(text)))) return L"";
    return text.c_str();
}
// Current-build adapter. Derive the owner/UID/GUID field accesses from the
// symbol-matched Identity() implementation and the verified interface thunk.
// If either instruction pattern differs, retain the opaque Windows identity.
static std::wstring PersistentIdentity(winrt::Windows::Foundation::IInspectable const& model) {
    auto icon = UdkIcon(model); if (!icon) return L"";
    HMODULE module = GetModuleHandleW(L"Taskbar.dll"); if (!module) return L"";
    auto const& symbols = CachedSymbols(module);
    static const BYTE* identity = nullptr; static const BYTE* getter = nullptr; static bool resolved = false;
    static std::mutex identityLock;
    std::unique_lock<std::mutex> identityGuard(identityLock);
    if (!resolved) {
        resolved = true;
        for (auto const& entry : symbols) {
            if (entry.first == Normalize(L"public: struct NotificationAreaIconIdentity __cdecl winrt::WindowsUdk::UI::Shell::implementation::NotificationAreaIcon2::Identity(void)const")) identity = reinterpret_cast<BYTE*>(module) + entry.second;
            if (entry.first == Normalize(L"public: virtual int __cdecl winrt::impl::produce<struct winrt::WindowsUdk::UI::Shell::implementation::NotificationAreaIcon2,struct winrt::WindowsUdk::UI::Shell::INotificationAreaIcon6>::get_AppFriendlyName(void * *)")) getter = reinterpret_cast<BYTE*>(module) + entry.second;
        }
    }
    identityGuard.unlock();
    if (!identity || !getter) return L"";
    const BYTE expectedIdentity[] = {0x40,0x53,0x48,0x83,0xEC,0x30,0x48,0x8B,0x41,0x50,0x48,0x8B,0xDA,0x48,0x89,0x02,0x8B,0x41,0x58,0x89,0x42,0x08,0x0F,0x10,0x41,0x5C,0xF3,0x0F,0x7F,0x42,0x0C};
    const BYTE expectedGetter[] = {0x40,0x53,0x48,0x83,0xEC,0x20,0x48,0x8B,0xDA,0x48,0x83,0x22,0x00,0x48,0x8D,0x41,0xC8};
    if (memcmp(identity, expectedIdentity, sizeof(expectedIdentity)) || memcmp(getter, expectedGetter, sizeof(expectedGetter))) return L"";
    constexpr GUID iid{0xD76CAD80,0x3103,0x54F4,{0x9A,0xBE,0x5F,0x04,0x7C,0xBF,0xE8,0x5C}};
    winrt::com_ptr<IUnknown> iface;
    reinterpret_cast<IUnknown*>(winrt::get_abi(icon))->QueryInterface(iid, iface.put_void());
    if (!iface || (*reinterpret_cast<void***>(iface.get()))[6] != getter) return L"";
    auto owner = reinterpret_cast<BYTE*>(iface.get()) + static_cast<signed char>(getter[16]);
    HWND window = *reinterpret_cast<HWND*>(owner + identity[9]);
    UINT uid = *reinterpret_cast<UINT*>(owner + identity[18]);
    GUID guid = *reinterpret_cast<GUID*>(owner + identity[25]);
    if (guid != GUID{}) { wchar_t value[40]{}; StringFromGUID2(guid, value, 40); return std::wstring(L"guid:") + value; }
    DWORD pid{}; GetWindowThreadProcessId(window, &pid);
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid); if (!process) return L"";
    wchar_t executable[32768]{}; DWORD size = 32768;
    bool ok = QueryFullProcessImageNameW(process, 0, executable, &size); CloseHandle(process);
    if (!ok) return L"";
    std::wstring path(executable, size); std::transform(path.begin(), path.end(), path.begin(), towlower);
    return L"app:" + path + L"#" + std::to_wstring(uid);
}
static winrt::Windows::Foundation::IInspectable ContentModel(FrameworkElement const& element) {
    void* content = nullptr;
    if (FAILED(ModelGetter(element.DataContext(), L"IIconViewModel", L"get_ContentVM", &content)) || !content) return nullptr;
    return winrt::Windows::Foundation::IInspectable{content, winrt::take_ownership_from_abi};
}
static std::wstring IconIdentity(FrameworkElement const& element) {
    if (winrt::get_class_name(element) == L"SystemTray.NotifyIconView") {
        auto stable = PersistentIdentity(element.DataContext()); if (!stable.empty()) return stable;
    }
    auto configuration = ObjectGetter(element.DataContext(), L"IIconViewModel", L"get_Configuration");
    GUID configuredId{};
    if (configuration && SUCCEEDED(ModelGetter(configuration, L"IIconConfiguration", L"get_Id", &configuredId))) {
        wchar_t text[40]{}; StringFromGUID2(configuredId, text, 40); return text;
    }
    auto content = ContentModel(element);
    GUID id{};
    if (content && SUCCEEDED(ModelGetter(content, L"IIconContentViewModel", L"get_Id", &id))) {
        wchar_t text[40]{}; StringFromGUID2(id, text, 40); return text;
    }
    return L"";
}

static JsonObject bridgeSettings{nullptr};
static std::wstring ElementText(FrameworkElement const& element) {
    return winrt::Windows::UI::Xaml::Automation::AutomationProperties::GetName(element).c_str();
}
static bool Included(JsonObject const& rule, std::wstring const& identity) {
    if (rule.GetNamedBoolean(L"allApps", true)) return true;
    for (auto const& value : rule.GetNamedArray(L"icons", JsonArray{})) if (value.GetString() == identity) return true;
    return false;
}
static bool ExplicitlyExcluded(JsonObject const& rule, std::wstring const& identity) {
    if (rule.GetNamedBoolean(L"allApps", true)) return false;
    for (auto const& value : rule.GetNamedArray(L"excludedIcons", JsonArray{})) if (value.GetString() == identity) return true;
    return false;
}
static bool AssignedElsewhere(std::wstring const& identity) {
    for (auto const& value : bridgeSettings.GetNamedArray(L"monitors", JsonArray{})) {
        auto rule = value.GetObject();
        if (rule.GetNamedBoolean(L"enabled", true) && Included(rule, identity)) return true;
    }
    return false;
}
static void RestoreBridgeVisibility(FrameworkElement const& element) {
    auto property = UIElement::VisibilityProperty();
    auto& changes = g_trayPropertyChanges;
    auto found = std::find_if(changes.begin(), changes.end(), [&](TrayPropertyChange const& change) { return change.element.get() == element && change.property == property; });
    if (found == changes.end()) return;
    auto saved = *found; changes.erase(found);
    if (saved.originalBinding) Data::BindingOperations::SetBinding(element, property, saved.originalBinding);
    else if (saved.originalValue == DependencyProperty::UnsetValue()) element.ClearValue(property);
    else element.SetValue(property, saved.originalValue);
}
static void FilterTree(DependencyObject const& parent, JsonObject const& rule, bool primary, int depth = 0) {
    if (!parent || depth > 20) return;
    if (auto element = parent.try_as<FrameworkElement>()) {
        auto type = winrt::get_class_name(element);
        bool enabled = rule.GetNamedBoolean(L"enabled", true);
        bool visible = true; bool filter = false;
        if (type == L"SystemTray.NotifyIconView") {
            auto id = IconIdentity(element); auto text = ElementText(element);
            bool management = text.find(L"TrayBridge") == 0;
            filter = true;
            visible = management ? primary : (enabled && Included(rule, id));
            // New/unassigned icons remain recoverable on primary.
            if (!management && primary && !AssignedElsewhere(id) && !ExplicitlyExcluded(rule, id)) visible = true;
            // Collapse the item container as well, avoiding empty icon slots.
            auto item = Media::VisualTreeHelper::GetParent(element).try_as<FrameworkElement>();
            while (item && winrt::get_class_name(item) != L"Windows.UI.Xaml.Controls.ContentPresenter" && !item.try_as<Controls::ListViewItem>()) item = Media::VisualTreeHelper::GetParent(item).try_as<FrameworkElement>();
            if (item) SetElementVisibility(item, L"TrayBridge app container", visible);
        } else if (element.Name() == L"ControlCenterButton") { filter = true; visible = enabled && rule.GetNamedBoolean(L"quickSettings", true); }
        else if (element.Name() == L"NotificationCenterButton" || element.Name() == L"SecondaryClockStack") {
            visible = enabled && rule.GetNamedBoolean(L"clock", true); filter = !visible;
            if (visible) RestoreBridgeVisibility(element);
        }
        else if (element.Name() == L"MainStack" || element.Name() == L"NonActivatableStack") {
            visible = enabled && rule.GetNamedBoolean(L"indicators", true); filter = !visible;
            if (visible && primary) RestoreBridgeVisibility(element);
        }
        else if (type == L"SystemTray.IconView") {
            auto id = IconIdentity(element);
            if (!id.empty()) {
                filter = true; visible = rule.GetNamedBoolean(L"allSystemIcons", true);
                for (auto const& value : rule.GetNamedArray(L"systemIcons", JsonArray{})) if (value.GetString() == id) visible = true;
                if (visible) { RestoreBridgeVisibility(element); filter = false; }
            }
        }
        if (filter) SetElementVisibility(element, L"TrayBridge routing", visible);
    }
    int count = Media::VisualTreeHelper::GetChildrenCount(parent);
    for (int i = 0; i < count; ++i) FilterTree(Media::VisualTreeHelper::GetChild(parent, i), rule, primary, depth + 1);
}
void ApplyBridgeStyle(XamlRoot const& root, HWND window) {
    if (!bridgeSettings) return;
    bool primary = IsPrimaryTaskbarWindow(window);
    MONITORINFOEXW info{}; info.cbSize = sizeof(info);
    GetMonitorInfoW(GetActualMonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &info);
    JsonObject rule;
    for (auto const& value : bridgeSettings.GetNamedArray(L"monitors", JsonArray{})) {
        auto candidate = value.GetObject();
        if (candidate.GetNamedString(L"device", L"") == info.szDevice) { rule = candidate; break; }
    }
    FilterTree(root.Content(), rule, primary);
    root.Content().try_as<FrameworkElement>().UpdateLayout();
}

static std::string Utf8(std::wstring const& value) {
    int length = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string result(length, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
    return result;
}
void SaveJson(std::wstring const& name, JsonObject const& object) {
    auto destination = std::filesystem::path(bridgeDirectory) / name;
    auto temporary = destination; temporary += L".tmp";
    { std::ofstream out(temporary, std::ios::binary | std::ios::trunc); out << Utf8(object.Stringify().c_str()); }
    MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}
static JsonObject Describe(FrameworkElement element) {
    JsonObject item;
    item.Insert(L"class", JsonValue::CreateStringValue(winrt::get_class_name(element)));
    item.Insert(L"name", JsonValue::CreateStringValue(element.Name()));
    auto text = winrt::Windows::UI::Xaml::Automation::AutomationProperties::GetName(element);
    if (text.empty()) {
        auto tooltip = Controls::ToolTipService::GetToolTip(element);
        if (auto tip = tooltip.try_as<winrt::Windows::Foundation::IPropertyValue>()) {
            if (tip.Type() == winrt::Windows::Foundation::PropertyType::String) text = tip.GetString();
        }
    }
    item.Insert(L"text", JsonValue::CreateStringValue(text));
    item.Insert(L"visible", JsonValue::CreateBooleanValue(element.Visibility() == Visibility::Visible));
    item.Insert(L"width", JsonValue::CreateNumberValue(element.ActualWidth()));
    if (winrt::get_class_name(element) == L"SystemTray.NotifyIconView" || winrt::get_class_name(element) == L"SystemTray.IconView") {
        item.Insert(L"identity", JsonValue::CreateStringValue(IconIdentity(element)));
        if (winrt::get_class_name(element) == L"SystemTray.NotifyIconView") item.Insert(L"appName", JsonValue::CreateStringValue(AppName(element.DataContext())));
        if (auto content = ContentModel(element)) {
            item.Insert(L"contentClass", JsonValue::CreateStringValue(winrt::get_class_name(content)));
            void* metadata = nullptr;
            if (SUCCEEDED(ModelGetter(content, L"IIconContentViewModel", L"get_ContentMetadata", &metadata)) && metadata) {
                winrt::Windows::Foundation::IInspectable object{metadata, winrt::take_ownership_from_abi};
                item.Insert(L"metadataClass", JsonValue::CreateStringValue(winrt::get_class_name(object)));
            }
        }
    }
    auto model = element.DataContext();
    if (model) {
        item.Insert(L"modelClass", JsonValue::CreateStringValue(winrt::get_class_name(model)));
        auto provider = model.try_as<winrt::Windows::UI::Xaml::Data::ICustomPropertyProvider>();
        if (provider) {
            JsonObject properties;
            for (auto name : {L"Id", L"IconId", L"Guid", L"GuidItem", L"Tooltip", L"ToolTip", L"ToolTipText", L"AutomationPropertyName", L"Text", L"Name", L"ExecutablePath", L"ProcessId", L"WindowHandle", L"IconName", L"DisplayName"}) {
                try {
                    auto property = provider.GetCustomProperty(name);
                    if (property && property.CanRead()) {
                        auto value = property.GetValue(model);
                        if (auto stringValue = value.try_as<winrt::Windows::Foundation::IPropertyValue>()) {
                            if (stringValue.Type() == winrt::Windows::Foundation::PropertyType::String) properties.Insert(name, JsonValue::CreateStringValue(stringValue.GetString()));
                            else if (stringValue.Type() == winrt::Windows::Foundation::PropertyType::UInt32) properties.Insert(name, JsonValue::CreateNumberValue(stringValue.GetUInt32()));
                            else if (stringValue.Type() == winrt::Windows::Foundation::PropertyType::UInt64) properties.Insert(name, JsonValue::CreateStringValue(std::to_wstring(stringValue.GetUInt64())));
                        }
                    }
                } catch (...) {}
            }
            item.Insert(L"properties", properties);
        }
    }
    return item;
}
static void CollectTree(DependencyObject const& parent, JsonArray const& elements, int depth) {
    if (!parent || depth > 18 || elements.Size() > 1200) return;
    if (auto element = parent.try_as<FrameworkElement>()) {
        try { auto item = Describe(element); item.Insert(L"depth", JsonValue::CreateNumberValue(depth)); elements.Append(item); } catch (...) {}
    }
    int count = Media::VisualTreeHelper::GetChildrenCount(parent);
    for (int i = 0; i < count; ++i) CollectTree(Media::VisualTreeHelper::GetChild(parent, i), elements, depth + 1);
}
void InspectOnTaskbarThread(void*) {
    JsonArray taskbars;
    EnumThreadWindows(GetCurrentThreadId(), [](HWND window, LPARAM context) -> BOOL {
        wchar_t type[64]{}; GetClassNameW(window, type, 64);
        bool primary = wcscmp(type, L"Shell_TrayWnd") == 0;
        if (!primary && wcscmp(type, L"Shell_SecondaryTrayWnd") != 0) return TRUE;
        JsonObject taskbar; JsonArray elements;
        auto root = primary ? GetTaskbarXamlRoot(window) : GetSecondaryTaskbarXamlRoot(window);
        if (root) CollectTree(root.Content(), elements, 0);
        MONITORINFOEXW info{}; info.cbSize = sizeof(info);
        GetMonitorInfoW(GetActualMonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &info);
        taskbar.Insert(L"device", JsonValue::CreateStringValue(info.szDevice));
        taskbar.Insert(L"primary", JsonValue::CreateBooleanValue(primary));
        taskbar.Insert(L"elements", elements);
        reinterpret_cast<JsonArray*>(context)->Append(taskbar);
        return TRUE;
    }, reinterpret_cast<LPARAM>(&taskbars));
    JsonObject snapshot; snapshot.Insert(L"taskbars", taskbars);
    SaveJson(L"inventory.json", snapshot);
}
static void Status(PCWSTR value) {
    JsonObject object; object.Insert(L"state", JsonValue::CreateStringValue(value));
    object.Insert(L"detail", JsonValue::CreateStringValue(bridgeStatus));
    object.Insert(L"pid", JsonValue::CreateNumberValue(GetCurrentProcessId()));
    wchar_t process[MAX_PATH]{}; GetModuleFileNameW(nullptr, process, MAX_PATH);
    auto name = std::filesystem::path(process).stem().wstring();
    SaveJson(L"status-" + name + L".json", object);
}
extern "C" __declspec(dllexport) DWORD WINAPI TrayBridgeInspect(void* argument) {
    if (bridgeRunning.exchange(true)) return 0;
    bridgeStop.store(false); bridgeDirectory = static_cast<wchar_t*>(argument);
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        g_targetProcess = GetTargetProcess();
        if (g_targetProcess == TargetProcess::Other) { bridgeStatus = L"Only Explorer and ShellHost are supported."; Status(L"error"); bridgeRunning.store(false); return 1; }
        MH_Initialize();
        if (!HookTaskbarDllSymbols()) { Status(L"error"); MH_Uninitialize(); bridgeRunning.store(false); return 2; }
        auto window = FindCurrentProcessTaskbarWnd();
        if (window) RunFromWindowThread(window, InspectOnTaskbarThread, nullptr);
        Status(L"inspected");
        MH_DisableHook(MH_ALL_HOOKS); bridgeRunning.store(false);
        return 0;
    } catch (winrt::hresult_error const& error) { bridgeStatus = error.message().c_str(); Status(L"error"); bridgeRunning.store(false); return 3; }
}
static void TickOnTaskbarThread(void* argument) {
    bridgeSettings = *reinterpret_cast<JsonObject*>(argument);
    ApplySettingsFromTaskbarThread(nullptr);
    InspectOnTaskbarThread(nullptr);
}
static JsonObject ReadSettings() {
    std::ifstream file(std::filesystem::path(bridgeDirectory) / L"native-settings.json", std::ios::binary);
    std::string input((std::istreambuf_iterator<char>(file)), {});
    if (input.compare(0, 3, "\xEF\xBB\xBF") == 0) input.erase(0, 3);
    return JsonObject::Parse(winrt::to_hstring(input));
}
#include "Overflow.cpp"
static void ResidentWorker() {
    bool initialized = false;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        auto settings = ReadSettings();
        DWORD controller = static_cast<DWORD>(settings.GetNamedNumber(L"controllerPid", 0));
        HANDLE lease = OpenProcess(SYNCHRONIZE, FALSE, controller);
        if (!lease) throw std::runtime_error("The TrayBridge controller is unavailable.");
        struct Lease { HANDLE value; ~Lease() { CloseHandle(value); } } leaseOwner{lease};
        MH_Initialize();
        if (!Wh_ModInit() || (IsExplorerTarget() && !HookBridgeOverflow()) || !Wh_ApplyHookOperations() || MH_EnableHook(MH_ALL_HOOKS) != MH_OK) { g_modUnloading.store(1); MH_DisableHook(MH_ALL_HOOKS); Status(L"error"); bridgeRunning.store(false); return; }
        initialized = true;
        if (IsExplorerTarget()) { JsonObject snapshot; snapshot.Insert(L"icons", JsonArray{}); SaveJson(L"hidden-inventory.json", snapshot); }
        if (auto window = FindCurrentProcessTaskbarWnd()) RunFromWindowThread(window, TickOnTaskbarThread, &settings);
        Wh_ModAfterInit(); Status(L"active");
        while (!bridgeStop.load() && WaitForSingleObject(lease, 500) == WAIT_TIMEOUT) {
            settings = ReadSettings();
            if (!settings.GetNamedBoolean(L"enabled", false)) break;
            auto window = FindCurrentProcessTaskbarWnd();
            if (window) RunFromWindowThread(window, TickOnTaskbarThread, &settings);
            if (auto overflowWindow = overflowDispatchWindow.load()) RunFromWindowThread(overflowWindow, [](void*) { try { RefreshOverflow(); } catch (...) {} }, nullptr);
        }
        if (auto overflowWindow = overflowDispatchWindow.load()) RunFromWindowThread(overflowWindow, RestoreBridgeOverflow, nullptr);
        Wh_ModBeforeUninit();
        // Keep trampolines and this DLL resident: other shell threads may still
        // be returning through a hook. Disabled hooks are safe to re-enable.
        MH_DisableHook(MH_ALL_HOOKS);
        Status(L"stopped");
    } catch (winrt::hresult_error const& error) {
        bridgeStatus = error.message().c_str();
        if (initialized) { if (auto overflowWindow = overflowDispatchWindow.load()) RunFromWindowThread(overflowWindow, RestoreBridgeOverflow, nullptr); Wh_ModBeforeUninit(); }
        MH_DisableHook(MH_ALL_HOOKS); Status(L"error");
    } catch (std::exception const& error) {
        bridgeStatus = winrt::to_hstring(error.what()).c_str();
        if (initialized) { if (auto overflowWindow = overflowDispatchWindow.load()) RunFromWindowThread(overflowWindow, RestoreBridgeOverflow, nullptr); Wh_ModBeforeUninit(); }
        MH_DisableHook(MH_ALL_HOOKS); Status(L"error");
    }
    bridgeRunning.store(false);
}
extern "C" __declspec(dllexport) DWORD WINAPI TrayBridgeInitialize(void* argument) {
    if (bridgeRunning.exchange(true)) return 0;
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    bridgeDirectory = static_cast<wchar_t*>(argument); bridgeStop.store(false); bridgeStatus.clear();
    Status(L"starting");
    std::thread(ResidentWorker).detach();
    winrt::uninit_apartment();
    return 0;
}
extern "C" __declspec(dllexport) DWORD WINAPI TrayBridgeStop(void*) {
    bridgeStop.store(true);
    for (int i = 0; bridgeRunning.load() && i < 250; ++i) Sleep(100);
    return bridgeRunning.load() ? 4 : 0;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
