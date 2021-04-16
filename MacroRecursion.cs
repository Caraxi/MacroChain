using System;
using System.Linq;
using Dalamud.Hooking;
using Dalamud.Plugin;
using FFXIVClientInterface;
using FFXIVClientInterface.Client.UI.Misc;

namespace MacroRecursion {
    public unsafe class MacroRecursion : IDalamudPlugin {

        private ClientInterface ci;
        
        public string Name => "Macro Chain";
        private DalamudPluginInterface pluginInterface;
        
        private delegate void MacroCallDelegate(RaptureShellModuleStruct* a, RaptureMacroModuleStruct.Macro* b);

        private Hook<MacroCallDelegate> macroCallHook;
        
        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.pluginInterface = pluginInterface;

            ci = new ClientInterface(pluginInterface.TargetModuleScanner, pluginInterface.Data);

            try {
                var macroCallPtr = pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8D 4D 28");
                macroCallHook = new Hook<MacroCallDelegate>(macroCallPtr, new MacroCallDelegate(MacroCallDetour));
                macroCallHook?.Enable();

                pluginInterface.CommandManager.AddHandler("/nextmacro", new Dalamud.Game.Command.CommandInfo(OnMacroCommandHandler) {
                    HelpMessage = "Executes the next macro.",
                    ShowInHelp = true
                });
                pluginInterface.CommandManager.AddHandler("/runmacro", new Dalamud.Game.Command.CommandInfo(OnRunMacroCommand) {
                    HelpMessage = "Execute a macro (Not usable inside macros). - /runmacro ## [individual|shared].",
                    ShowInHelp = true
                });
                
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
            
        }
        
        private bool MacroLock {
            set => ci.UiModule.RaptureShellModule.Data->MacroLockState = (byte) (value ? 1 : 0);
            get => ci.UiModule.RaptureShellModule.Data->MacroLockState != 0;
        }

        public void Dispose() {
            pluginInterface.CommandManager.RemoveHandler("/nextmacro");
            pluginInterface.CommandManager.RemoveHandler("/runmacro");
            macroCallHook?.Disable();
            macroCallHook?.Dispose();
        }

        private RaptureMacroModuleStruct.Macro* lastExecutedMacro = null;
        private RaptureMacroModuleStruct.Macro* nextMacro = null;
        private RaptureMacroModuleStruct.Macro* downMacro = null;

        private void MacroCallDetour(RaptureShellModuleStruct* raptureShellModule, RaptureMacroModuleStruct.Macro* macro) {
            macroCallHook?.Original(raptureShellModule, macro);
            if (MacroLock) return;
            lastExecutedMacro = macro;
            nextMacro = null;
            downMacro = null;
            if (lastExecutedMacro == ci.UiModule.RaptureMacroModule.Data->Individual[99] || lastExecutedMacro == ci.UiModule.RaptureMacroModule.Data->Shared[99]) {
                return;
            }

            nextMacro = macro + 1;
            for (var i = 90; i < 100; i++) {
                if (lastExecutedMacro == ci.UiModule.RaptureMacroModule.Data->Individual[i] || lastExecutedMacro == ci.UiModule.RaptureMacroModule.Data->Shared[i]) {
                    return;
                }
            }

            downMacro = macro + 10;
        }
        
        public void OnMacroCommandHandler(string command, string args) {
            try {
                if (ci.UiModule.RaptureShellModule.Data->MacroCurrentLine < 0) {
                    pluginInterface.Framework.Gui.Chat.PrintError("No macro is running.");
                    return;
                }

                if (args.ToLower() == "down") {
                    if (downMacro != null) {
                        MacroLock = false;
                        MacroCallDetour(ci.UiModule.RaptureShellModule.Data, downMacro);
                    } else
                        pluginInterface.Framework.Gui.Chat.PrintError("Can't use `/nextmacro down` on macro 90+");
                } else {
                    if (nextMacro != null) {
                        MacroLock = false;
                        MacroCallDetour(ci.UiModule.RaptureShellModule.Data, nextMacro);
                    } else
                        pluginInterface.Framework.Gui.Chat.PrintError("Can't use `/nextmacro` on macro 99.");
                }
                MacroLock = false;
                
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }

        public void OnRunMacroCommand(string command, string args) {
            try {
                if (ci.UiModule.RaptureShellModule.Data->MacroCurrentLine >= 0) {
                    pluginInterface.Framework.Gui.Chat.PrintError("/runmacro is not usable while macros are running. Please use /nextmacro");
                    return;
                }
                var argSplit = args.Split(' ');
                var num = byte.Parse(argSplit[0]);

                if (num > 99) {
                    pluginInterface.Framework.Gui.Chat.PrintError("Invalid Macro number.\nShould be 0 - 99");
                    return;
                }

                var shared = false;
                foreach (var arg in argSplit.Skip(1)) {
                    switch (arg.ToLower()) {
                        case "shared":
                        case "share":
                        case "s": {
                            shared = true;
                            break;
                        }
                        case "individual":
                        case "i": {
                            shared = false;
                            break;
                        }
                    }
                }
                PluginLog.Log($"{(ulong)ci.UiModule.RaptureShellModule.Data:X}");
                MacroCallDetour(ci.UiModule.RaptureShellModule.Data, (shared ? ci.UiModule.RaptureMacroModule.Data->Shared : ci.UiModule.RaptureMacroModule.Data->Individual)[num]);
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }
    }
}
