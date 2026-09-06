ScriptName BankPrismNative Hidden
{Native bridge to the BankPrismNative SKSE plugin.
 Signatures must match RegisterFuncs() in SKSE_Source/src/main.cpp.}

; Creates (or re-shows) the PrismaUI view and focuses it, pausing the game.
Function OpenMenu() global native

; Unfocuses and hides the view, unpausing the game.
Function CloseMenu() global native

; Pushes a JSON payload to the view via the bankUpdateParams interop call.
Function UpdateParams(String jsonPayload) global native
