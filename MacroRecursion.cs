using System;
using Dalamud.Hooking;
using Dalamud.Plugin;
using FFXIVClientInterface;
using FFXIVClientInterface.Client.UI.Misc;

namespace MacroRecursion {
    public unsafe class MacroRecursion : IDalamudPlugin {

        private ClientInterface ci;
        
        public string Name => "Macro Fallthrough";
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
                    HelpMessage = "Execute the next macro.",
                    ShowInHelp = true
                });
                
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
            
        }
        
        private bool MacroLock {
            set => ci.UiModule.RaptureShellModule.Data->MacroLockState = (byte) (value ? 1 : 0);
        }

        public void Dispose() {
            pluginInterface.CommandManager.RemoveHandler("/nextmacro");
            macroCallHook?.Disable();
            macroCallHook?.Dispose();
        }

        private RaptureMacroModuleStruct.Macro* lastExecutedMacro = null;
        private RaptureMacroModuleStruct.Macro* nextMacro = null;
        private RaptureMacroModuleStruct.Macro* downMacro = null;

        private void MacroCallDetour(RaptureShellModuleStruct* raptureShellModule, RaptureMacroModuleStruct.Macro* macro) {
            macroCallHook?.Original(raptureShellModule, macro);
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
                MacroLock = false;
                if (args.ToLower() == "down") {
                    if (downMacro != null)
                        MacroCallDetour(ci.UiModule.RaptureShellModule.Data, downMacro);
                    else
                        pluginInterface.Framework.Gui.Chat.PrintError("Can't use `/nextmacro down` on macro 90+");
                } else {
                    if (nextMacro != null)
                        MacroCallDetour(ci.UiModule.RaptureShellModule.Data, nextMacro);
                    else
                        pluginInterface.Framework.Gui.Chat.PrintError("Can't use `/nextmacro` on macro 99.");
                }
                MacroLock = false;
                
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }
    }
}
