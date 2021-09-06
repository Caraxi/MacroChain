using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace MacroChain {
    public sealed unsafe class MacroChain : IDalamudPlugin {

        [PluginService] public static CommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static Framework Framework { get; private set; } = null!;

        [PluginService] public static ClientState ClientState { get; private set; } = null!;
        [PluginService] public static ChatGui Chat { get; private set; } = null!;

        public string Name => "Macro Chain";

        private delegate void MacroCallDelegate(RaptureShellModule* raptureShellModule, RaptureMacroModule.Macro* macro);

        private Hook<MacroCallDelegate> macroCallHook;

        private bool isDisposed;

        public MacroChain() {
            Task.Run(() => {
                FFXIVClientStructs.Resolver.Initialize();
                if (isDisposed) return;
                try {
                    macroCallHook = new Hook<MacroCallDelegate>(new IntPtr(RaptureShellModule.fpExecuteMacro), MacroCallDetour);
                    macroCallHook?.Enable();

                    CommandManager.AddHandler("/nextmacro", new Dalamud.Game.Command.CommandInfo(OnMacroCommandHandler) {
                        HelpMessage = "Executes the next macro.",
                        ShowInHelp = true
                    });
                    CommandManager.AddHandler("/runmacro", new Dalamud.Game.Command.CommandInfo(OnRunMacroCommand) {
                        HelpMessage = "Execute a macro (Not usable inside macros). - /runmacro ## [individual|shared].",
                        ShowInHelp = true
                    });

                    Framework.Update += FrameworkUpdate;
                } catch (Exception ex) {
                    PluginLog.LogError(ex.ToString());
                }
            });
        }

        public void Dispose() {
            isDisposed = true;
            CommandManager.RemoveHandler("/nextmacro");
            CommandManager.RemoveHandler("/runmacro");
            macroCallHook?.Disable();
            macroCallHook?.Dispose();
            macroCallHook = null;
            Framework.Update -= FrameworkUpdate;
        }

        private RaptureMacroModule.Macro* lastExecutedMacro = null;
        private RaptureMacroModule.Macro* nextMacro = null;
        private RaptureMacroModule.Macro* downMacro = null;
        private readonly Stopwatch paddingStopwatch = new Stopwatch();

        private void MacroCallDetour(RaptureShellModule* raptureShellModule, RaptureMacroModule.Macro* macro) {
            macroCallHook?.Original(raptureShellModule, macro);
            if (RaptureShellModule.Instance->MacroLocked) return;
            lastExecutedMacro = macro;
            nextMacro = null;
            downMacro = null;
            if (lastExecutedMacro == RaptureMacroModule.Instance->Individual[99] || lastExecutedMacro == RaptureMacroModule.Instance->Shared[99]) {
                return;
            }

            nextMacro = macro + 1;
            for (var i = 90; i < 100; i++) {
                if (lastExecutedMacro == RaptureMacroModule.Instance->Individual[i] || lastExecutedMacro == RaptureMacroModule.Instance->Shared[i]) {
                    return;
                }
            }

            downMacro = macro + 10;
        }
        
        public void OnMacroCommandHandler(string command, string args) {
            try {
                if (lastExecutedMacro == null) {
                    Chat.PrintError("No macro is running.");
                    return;
                }

                if (args.ToLower() == "down") {
                    if (downMacro != null) {
                        RaptureShellModule.Instance->MacroLocked = false;
                        RaptureShellModule.Instance->ExecuteMacro(downMacro);
                    } else
                        Chat.PrintError("Can't use `/nextmacro down` on macro 90+");
                } else {
                    if (nextMacro != null) {
                        RaptureShellModule.Instance->MacroLocked = false;
                        RaptureShellModule.Instance->ExecuteMacro(nextMacro);
                    } else
                        Chat.PrintError("Can't use `/nextmacro` on macro 99.");
                }
                RaptureShellModule.Instance->MacroLocked = false;
                
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }

        public void FrameworkUpdate(Framework framework) {
            if (lastExecutedMacro == null) return;
            if (ClientState == null) return;
            if (!ClientState.IsLoggedIn) {
                lastExecutedMacro = null;
                paddingStopwatch.Stop();
                paddingStopwatch.Reset();
                return;
            }
            if (RaptureShellModule.Instance->MacroCurrentLine >= 0) {
                paddingStopwatch.Restart();
                return;
            }

            if (paddingStopwatch.ElapsedMilliseconds > 2000) {
                lastExecutedMacro = null;
                paddingStopwatch.Stop();
                paddingStopwatch.Reset();
            }
        }

        public void OnRunMacroCommand(string command, string args) {
            try {
                if (lastExecutedMacro != null) {
                    Chat.PrintError("/runmacro is not usable while macros are running. Please use /nextmacro");
                    return;
                }
                var argSplit = args.Split(' ');
                var num = byte.Parse(argSplit[0]);

                if (num > 99) {
                    Chat.PrintError("Invalid Macro number.\nShould be 0 - 99");
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
                RaptureShellModule.Instance->ExecuteMacro((shared ? RaptureMacroModule.Instance->Shared : RaptureMacroModule.Instance->Individual)[num]);
            } catch (Exception ex) {
                PluginLog.LogError(ex.ToString());
            }
        }
    }
}
