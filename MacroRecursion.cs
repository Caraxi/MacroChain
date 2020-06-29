using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin;

namespace MacroRecursion {
    public class MacroRecursion : IDalamudPlugin {
        public string Name => "MacroRecursion";
        private DalamudPluginInterface pluginInterface;
        
        private delegate void MacroCallDelegate(IntPtr a, IntPtr b);

        private Hook<MacroCallDelegate> macroCallHook;
        
        private IntPtr macroBasePtr = IntPtr.Zero;
        private IntPtr macroDataPtr = IntPtr.Zero;
        
        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.pluginInterface = pluginInterface;

            try {
                var macroCallPtr = pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8D 4D 20 49 8B D6");
                macroCallHook = new Hook<MacroCallDelegate>(macroCallPtr, new MacroCallDelegate(MacroCallDetour));
                macroCallHook?.Enable();

                pluginInterface.CommandManager.AddHandler("/macro", new Dalamud.Game.Command.CommandInfo(OnMacroCommandHandler) {
                    HelpMessage = "Execute a Macro - /macro ## [individual/shared]",
                    ShowInHelp = true
                });
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }

        public void Dispose() {
            macroCallHook?.Disable();
            macroCallHook?.Dispose();
            pluginInterface.CommandManager.RemoveHandler("/macro");
        }

        private void MacroCallDetour(IntPtr a, IntPtr b) {
            macroCallHook?.Original(a, b);

            macroBasePtr = IntPtr.Zero;
            macroDataPtr = IntPtr.Zero;

            // Hack-y search for first macro lol
            var scanBack = b;
            var limit = 200;
            while (limit-- >= 0) {
                var macroDatHeaderCheck = Marshal.ReadInt64(scanBack, -40);
                if (macroDatHeaderCheck == 0x41442E4F5243414D) {
                    macroDataPtr = scanBack;
                    macroBasePtr = a;
                    return;
                }

                scanBack -= 0x688;
            }

            PluginLog.LogError("Failed to find Macro[0]");
        }

        public void OnMacroCommandHandler(string command, string args) {
            if (macroBasePtr != IntPtr.Zero && macroDataPtr != IntPtr.Zero) {
                var argSplit = args.Split(' ');

                var num = byte.Parse(argSplit[0]);

                if (num > 199) {
                    pluginInterface.Framework.Gui.Chat.PrintError("Invalid Macro number.\nShould be 0 - 99");
                    return;
                }
                
                if (num < 100 && argSplit.Length > 1) {
                    switch (argSplit[1].ToLower()) {
                        case "shared":
                        case "share":
                        case "s": {
                            num += 100;
                            break;
                        }
                        case "individual":
                        case "i": {
                            break;
                        }
                        default: {
                            pluginInterface.Framework.Gui.Chat.PrintError("Invalid Macro Page.\nUse 'shared' or 'individual'.");
                            return;
                        }
                    }
                }

                var macroPtr = macroDataPtr + 0x688 * num;
                PluginLog.Log($"Executing Macro #{num} @ {macroPtr}");
                macroCallHook.Original(macroBasePtr, macroPtr);
            } else {
                pluginInterface.Framework.Gui.Chat.PrintError("MacroRecursion is not ready.\nExecute a macro to finish setup.");
            }
        }
    }
}
