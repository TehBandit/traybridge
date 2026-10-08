// GPL-3.0-only. Discovery and projected-type Measure interception adapted
// from Ris Peng's system-tray-folders v1.0.0; upstream source in vendor/.
using Inspectable = winrt::Windows::Foundation::IInspectable;
using IconVector = winrt::Windows::Foundation::Collections::IObservableVector<Inspectable>;
struct BridgeOverflowState {
    winrt::weak_ref<Controls::Control> control;
    winrt::weak_ref<Controls::ItemsControl> items;
    Inspectable originalSource{nullptr};
    Data::Binding originalBinding{nullptr};
    IconVector displayed{nullptr};
    std::wstring device;
    bool primary{};
};
static BridgeOverflowState& overflowState = *new BridgeOverflowState();
static std::atomic<HWND> overflowDispatchWindow{nullptr};
static thread_local bool overflowShowing = false;
static thread_local POINT overflowPoint{};
static FrameworkElement FindOverflowItems(DependencyObject const& parent, int depth = 0) {
    if (!parent || depth > 12) return nullptr;
    if (auto element = parent.try_as<FrameworkElement>()) if (element.Name() == L"OverflowItemsControl") return element;
    int count = Media::VisualTreeHelper::GetChildrenCount(parent);
    for (int i = 0; i < count; ++i) if (auto found = FindOverflowItems(Media::VisualTreeHelper::GetChild(parent, i), depth + 1)) return found;
    return nullptr;
}
static JsonObject RuleForDevice(JsonObject const& settings, std::wstring const& device) {
    for (auto const& value : settings.GetNamedArray(L"monitors", JsonArray{})) {
        auto rule = value.GetObject(); if (rule.GetNamedString(L"device", L"") == device) return rule;
    }
    return JsonObject{};
}
static std::wstring ModelIdentity(Inspectable const& model) {
    auto stable = PersistentIdentity(model); if (!stable.empty()) return stable;
    auto content = ObjectGetter(model, L"IIconViewModel", L"get_ContentVM"); GUID id{};
    if (!content || FAILED(ModelGetter(content, L"IIconContentViewModel", L"get_Id", &id))) return L"";
    wchar_t text[40]{}; StringFromGUID2(id, text, 40); return text;
}
static std::wstring ModelText(Inspectable const& model) {
    auto content = ObjectGetter(model, L"IIconViewModel", L"get_ContentVM"); winrt::hstring text;
    if (content) ModelGetter(content, L"IIconContentViewModel", L"get_ToolTipText", winrt::put_abi(text));
    return text.c_str();
}
static void RefreshOverflow() {
    auto control = overflowState.control.get(); auto items = overflowState.items.get();
    if (!control || !items || IsModUnloading()) return;
    auto original = ObjectGetter(control, L"INotificationAreaOverflow", L"get_OverflowIcons").try_as<IconVector>();
    if (!original) return;
    auto settings = ReadSettings(); auto rule = RuleForDevice(settings, overflowState.device);
    JsonArray catalog; std::vector<Inspectable> selected;
    for (auto const& model : original) {
        auto id = ModelIdentity(model); auto text = ModelText(model);
        JsonObject icon; icon.Insert(L"class", JsonValue::CreateStringValue(L"SystemTray.NotifyIconView"));
        icon.Insert(L"identity", JsonValue::CreateStringValue(id)); icon.Insert(L"text", JsonValue::CreateStringValue(text));
        icon.Insert(L"appName", JsonValue::CreateStringValue(AppName(model))); catalog.Append(icon);
        bool include = rule.GetNamedBoolean(L"enabled", true) && Included(rule, id);
        bool assigned = false;
        for (auto const& value : settings.GetNamedArray(L"monitors", JsonArray{})) {
            auto other = value.GetObject(); if (other.GetNamedBoolean(L"enabled", true) && Included(other, id)) assigned = true;
        }
        if (overflowState.primary && !assigned) include = true;
        if (text.find(L"TrayBridge") == 0) include = overflowState.primary;
        if (include) selected.push_back(model);
    }
    JsonObject snapshot; snapshot.Insert(L"icons", catalog); snapshot.Insert(L"device", JsonValue::CreateStringValue(overflowState.device));
    snapshot.Insert(L"visible", JsonValue::CreateNumberValue(selected.size()));
    SaveJson(L"hidden-inventory.json", snapshot);
    if (!overflowState.displayed) overflowState.displayed = winrt::single_threaded_observable_vector<Inspectable>();
    bool same = selected.size() == overflowState.displayed.Size();
    if (same) for (unsigned i = 0; i < selected.size(); ++i) if (selected[i] != overflowState.displayed.GetAt(i)) { same = false; break; }
    if (!same) overflowState.displayed.ReplaceAll(selected);
    if (items.ItemsSource() != overflowState.displayed) items.ItemsSource(overflowState.displayed);
}
using OverflowShow = void(WINAPI*)(void*, POINT, int);
static OverflowShow overflowShowOriginal;
static void WINAPI OverflowShowHook(void* instance, POINT point, int kind) {
    overflowShowing = !IsModUnloading(); overflowPoint = point;
    overflowShowOriginal(instance, point, kind); overflowShowing = false;
}
using OverflowMeasure = void(WINAPI*)(void*, winrt::Windows::Foundation::Size const*);
static OverflowMeasure overflowMeasureOriginal;
static void WINAPI OverflowMeasureHook(void* projected, winrt::Windows::Foundation::Size const* size) {
    if (overflowShowing && !IsModUnloading()) try {
        auto control = reinterpret_cast<Inspectable const*>(projected)->try_as<Controls::Control>();
        if (control) {
            control.ApplyTemplate();
            auto items = FindOverflowItems(control).try_as<Controls::ItemsControl>();
            if (items) {
                if (overflowState.items.get() != items) {
                    overflowState.control = winrt::make_weak(control); overflowState.items = winrt::make_weak(items);
                    overflowState.originalSource = items.ReadLocalValue(Controls::ItemsControl::ItemsSourceProperty());
                    overflowState.originalBinding = GetTrayPropertyBinding(items, Controls::ItemsControl::ItemsSourceProperty());
                    overflowState.displayed = nullptr;
                }
                MONITORINFOEXW info{}; info.cbSize = sizeof(info);
                auto monitor = GetActiveProxyFlyoutMonitor();
                if (!monitor) monitor = MonitorFromPoint_Original ? MonitorFromPoint_Original(overflowPoint, MONITOR_DEFAULTTONEAREST) : MonitorFromPoint(overflowPoint, MONITOR_DEFAULTTONEAREST);
                GetMonitorInfoW(monitor, &info); overflowState.device = info.szDevice; overflowState.primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                EnumThreadWindows(GetCurrentThreadId(), [](HWND window, LPARAM) -> BOOL { overflowDispatchWindow.store(window); return FALSE; }, 0);
                RefreshOverflow();
            }
        }
    } catch (...) { Wh_Log(L"Overflow projection skipped: incompatible template."); }
    overflowMeasureOriginal(projected, size);
}
static bool HookBridgeOverflow() {
    WindhawkUtils::SYMBOL_HOOK hooks[] = {
        {{LR"(public: void __cdecl winrt::SystemTray::OverflowXamlIslandManager::Show(struct tagPOINT,enum winrt::WindowsUdk::UI::Shell::InputDeviceKind))"}, &overflowShowOriginal, OverflowShowHook, true},
        {{LR"(public: __cdecl winrt::impl::consume_Windows_UI_Xaml_IUIElement<struct winrt::SystemTray::NotificationAreaOverflow>::Measure(struct winrt::Windows::Foundation::Size const &)const )"}, &overflowMeasureOriginal, OverflowMeasureHook, true},
    };
    return HookSymbols(GetSystemTrayViewModule(), hooks, ARRAYSIZE(hooks));
}
static void RestoreBridgeOverflow(void*) {
    auto items = overflowState.items.get();
    if (items && items.ItemsSource() == overflowState.displayed) {
        auto property = Controls::ItemsControl::ItemsSourceProperty();
        if (overflowState.originalBinding) Data::BindingOperations::SetBinding(items, property, overflowState.originalBinding);
        else if (overflowState.originalSource == DependencyProperty::UnsetValue()) items.ClearValue(property);
        else items.SetValue(property, overflowState.originalSource);
    }
    overflowState = BridgeOverflowState{};
    overflowDispatchWindow.store(nullptr);
}
