#include "RE/Skyrim.h"
#include "SKSE/SKSE.h"
#include "SKSE/API.h"
#include "PrismaUI_API.h"
#include <string>
#include <thread>
#include <chrono>
#include <functional>

// Global references
static PRISMA_UI_API::IVPrismaUI1* g_prismaUI = nullptr;
static PrismaView g_view = 0;

namespace ViewState {
    bool Ready() {
        return g_view && g_prismaUI && g_prismaUI->IsValid(g_view);
    }

    void SetVisible(bool a_visible) {
        if (!Ready()) return;
        if (g_prismaUI->IsHidden(g_view) != a_visible) return;
        if (a_visible) {
            g_prismaUI->Show(g_view);
        } else {
            g_prismaUI->Hide(g_view);
        }
    }

    void SetFocused(bool a_focused) {
        if (!Ready()) return;
        if (g_prismaUI->HasFocus(g_view) == a_focused) return;
        if (a_focused) {
            // bPauseGame = true (pause game when bank UI is open)
            g_prismaUI->Focus(g_view, true, true); 
        } else {
            g_prismaUI->Unfocus(g_view);
        }
    }
}

namespace BankPrismNative {
    
    // Dispatch Action to Papyrus via ModEvent
    void DispatchAction(const std::string& a_action, float a_numArg = 0.0f) {
        SKSE::GetTaskInterface()->AddTask([a_action, a_numArg]() {
            auto* dispatcher = SKSE::GetModCallbackEventSource();
            if (!dispatcher) return;
            SKSE::ModCallbackEvent event;
            event.eventName = "BankPrismAction"; // Must match RegisterForModEvent in Papyrus
            event.strArg = RE::BSFixedString(a_action.c_str());
            event.numArg = a_numArg;
            event.sender = nullptr;
            dispatcher->SendEvent(&event);
        });
    }

    // Callback when JS calls window.chrome.webview.postMessage("...")
    void OnJSAction(const char* argument) {
        std::string rawMsg = argument ? argument : "";
        
        // Parse "action:amount" format
        std::string act = rawMsg;
        float amount = 0.0f;
        
        size_t colonPos = rawMsg.find(':');
        if (colonPos != std::string::npos) {
            act = rawMsg.substr(0, colonPos);
            try {
                amount = std::stof(rawMsg.substr(colonPos + 1));
            } catch (...) {
                amount = 0.0f;
            }
        }

        if (act == "close") {
            SKSE::GetTaskInterface()->AddTask([]() {
                ViewState::SetFocused(false);
                ViewState::SetVisible(false);
                SKSE::log::info("BankPrism UI closed and unfocused via UI button");
            });
        }
        
        DispatchAction(act, amount);
    }

    // Callback when HTML finishes loading
    void OnDomReady(PrismaView view) {
        SKSE::log::info("BankPrism DOM Ready");
        SKSE::GetTaskInterface()->AddTask([]() {
            ViewState::SetVisible(true);
            ViewState::SetFocused(true); // Game Paused
        });
    }

    void EnsureView() {
        if (g_view && g_prismaUI->IsValid(g_view)) {
            SKSE::GetTaskInterface()->AddTask([]() {
                ViewState::SetVisible(true);
                ViewState::SetFocused(true);
            });
            return;
        }

        g_view = g_prismaUI->CreateView("BankPrism/BankView.html", OnDomReady);
        g_prismaUI->RegisterJSListener(g_view, "bankAction", OnJSAction);
        SKSE::log::info("BankPrism: view created");
    }

    // ---- Papyrus entry points ----

    void OpenMenu(RE::StaticFunctionTag*) {
        if (!g_prismaUI) return;
        SKSE::GetTaskInterface()->AddTask([]() { EnsureView(); });
    }

    void CloseMenu(RE::StaticFunctionTag*) {
        if (!g_prismaUI) return;
        SKSE::GetTaskInterface()->AddTask([]() {
            ViewState::SetFocused(false);
            ViewState::SetVisible(false);
        });
    }

    void UpdateParams(RE::StaticFunctionTag*, RE::BSFixedString jsonPayload) {
        if (!g_prismaUI) return;
        SKSE::GetTaskInterface()->AddTask([payload = std::string(jsonPayload.c_str() ? jsonPayload.c_str() : "")]() {
            if (g_view && g_prismaUI->IsValid(g_view)) {
                g_prismaUI->InteropCall(g_view, "bankUpdateParams", payload.c_str());
            }
        });
    }

    bool RegisterFuncs(RE::BSScript::IVirtualMachine* a_vm) {
        a_vm->RegisterFunction("OpenMenu", "BankPrismNative", OpenMenu);
        a_vm->RegisterFunction("CloseMenu", "BankPrismNative", CloseMenu);
        a_vm->RegisterFunction("UpdateParams", "BankPrismNative", UpdateParams);
        return true;
    }
}

// SKSE Query
extern "C" __declspec(dllexport) constinit auto SKSEPlugin_Version = []() {
    SKSE::PluginVersionData v;
    v.PluginVersion(1);
    v.PluginName("BankPrismNative");
    v.AuthorName("Developer");
    v.UsesAddressLibrary(true);
    v.UsesStructsPost629(true);
    return v;
}();

void OnMessage(SKSE::MessagingInterface::Message* message) {
    if (message->type == SKSE::MessagingInterface::kPostLoad) {
        g_prismaUI = PRISMA_UI_API::RequestPluginAPI<PRISMA_UI_API::IVPrismaUI1>();
    }
}

extern "C" __declspec(dllexport) bool SKSEAPI SKSEPlugin_Load(const SKSE::LoadInterface* a_skse) {
    SKSE::Init(a_skse);
    
    auto messaging = SKSE::GetMessagingInterface();
    if (messaging) {
        messaging->RegisterListener(OnMessage);
    }
    
    auto papyrus = SKSE::GetPapyrusInterface();
    papyrus->Register(BankPrismNative::RegisterFuncs);
    
    return true;
}
