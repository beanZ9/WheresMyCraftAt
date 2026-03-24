using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using WheresMyCraftAt.CraftingMenu;
using WheresMyCraftAt.CraftingSequence;
using WheresMyCraftAt.Handlers;
using static WheresMyCraftAt.CraftingSequence.CraftingSequence;
using static WheresMyCraftAt.Enums.WheresMyCraftAt;
using Vector2N = System.Numerics.Vector2;

namespace WheresMyCraftAt;

public class WheresMyCraftAt : BaseSettingsPlugin<WheresMyCraftAtSettings>
{
    public static WheresMyCraftAt Main;

    public readonly Dictionary<SpecialSlot, Vector2N> SpecialSlotDimensionMap = new()
    {
        {
            SpecialSlot.CurrencyTab, new Vector2N(126f, 252f)
        },
        {
            SpecialSlot.EssenceTab, new Vector2N(127.2f, 254.4f)
        }
    };

    public Vector2 ClickWindowOffset;
    public SyncTask<bool> CurrentOperation;
    public Dictionary<int, (int passCount, int failCount, int totalCount)> CurrentOperationStepCountList = [];
    public int[,] CompletedCrafts = new int[5, 12];
    public Dictionary<string, int> CurrentOperationUsedItemsList = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private List<Keys> keysToRelease = [];
    public CancellationTokenSource OperationCts;
    public List<CraftingBase> SelectedCraftingSteps = [];
    public int ServerLatency;

    public WheresMyCraftAt()
    {
        Name = "Wheres My Craft At";
        OperationCts = new CancellationTokenSource();
    }

    public override bool Initialise()
    {
        Main = this;
        InitializeLogMessageFilters();
        RegisterHotkey(Settings.RunOptions.RunButton);
        RegisterHotkey(Settings.Debugging.ToggleLogWindow);
        keysToRelease = [Keys.LButton, Keys.RButton];

        foreach (var key in keysToRelease) Input.RegisterKey(key);

        return true;
    }
    private void InitializeLogMessageFilters()
    {
        AddIfNew(LogMessageType.Trace, (false, Color.LightGray));
        AddIfNew(LogMessageType.Debug, (false, Color.Cyan));
        AddIfNew(LogMessageType.Info, (false, Color.White));
        AddIfNew(LogMessageType.Warning, (true, Color.Yellow));
        AddIfNew(LogMessageType.Error, (true, Color.Red));
        AddIfNew(LogMessageType.Critical, (true, Color.DarkRed));
        AddIfNew(LogMessageType.Profiler, (false, Color.SkyBlue));
        AddIfNew(LogMessageType.Evaluation, (false, Color.Orange));
        AddIfNew(LogMessageType.Special, (false, Color.Magenta));
        AddIfNew(LogMessageType.ItemData, (false, Color.LimeGreen));
        AddIfNew(LogMessageType.EndSessionStats, (true, Color.Beige));
        AddIfNew(LogMessageType.ItemUse, (true, new Color(160, 238, 0, 255)));

        void AddIfNew(LogMessageType messageType, (bool enabled, Color color) value)
        {
            Settings.Debugging.LogMessageFilters.TryAdd(messageType, value);
        }
    }

    private static void RegisterHotkey(HotkeyNode hotkey)
    {
        Input.RegisterKey(hotkey);
        hotkey.OnValueChanged += () => Input.RegisterKey(hotkey);
    }

    public override Job Tick()
    {
        ClickWindowOffset = GameController.Window.GetWindowRectangle().TopLeft;
        ServerLatency = GameController.IngameState.ServerData.Latency;

        if (Settings.Debugging.ToggleLogWindow.PressedOnce())
        {
            Settings.Debugging.LogWindow.Value = !Settings.Debugging.LogWindow.Value;
        }

        if (!GameHandler.IsInGameCondition())
        {
            Stop();
            return null;
        }

        if (Settings.RunOptions.RunButton.PressedOnce())
        {
            if (CurrentOperation is not null)
            {
                // Immediate cancellation called, release all buttons.
                // TODO: Get some help on how the hell this works.
                // Seems to work, no complaints from big brains so far kekw
                Stop();
            }
            else
            {
                Logging.Logging.MessagesList.Clear();
                Logging.Logging.LogMessage("Attempting to Start New Operation.", LogMessageType.Info);
                CurrentOperationUsedItemsList = [];
                CurrentOperationStepCountList = [];
                CompletedCrafts = new int[5, 12];
                ResetCancellationTokenSource();
                CurrentOperation = AsyncStart(OperationCts.Token);
            }
        }

        return null;
    }

    public override void Render()
    {
        base.Render();

        if (CurrentOperation is not null)
        {
            TaskUtils.RunOrRestart(ref CurrentOperation, () => null);
        }

        if (Settings.Debugging.InspectCraftedItem &&
            StashHandler.TryGetStashSpecialSlot(SpecialSlot.CurrencyTab, out var item))
        {
            GameController.InspectObject(FilterHandler.GetItemData(item.Item), "Crafter item data");
        }

        Logging.Logging.Render();
    }

    public void Stop()
    {
        if (CurrentOperation is null)
        {
            return;
        }

        CurrentOperation = null;

        foreach (var key in keysToRelease.Where(Input.GetKeyState)) Input.KeyUp(key);

        if (ItemHandler.IsItemRightClickedCondition())
        {
            Input.KeyPressRelease(Keys.Escape);
        }

        Logging.Logging.LogMessage("Stop() has been ran.", LogMessageType.Warning);
        Logging.Logging.LogEndCraftingStats();

        if (Settings.Debugging.AutoFullLogDumpOnEnd)
        {
            Logging.Logging.SaveLog(Logging.Logging.CombineLogsToString(true));
        }
    }

    private void ResetCancellationTokenSource()
    {
        if (OperationCts != null)
        {
            if (!OperationCts.IsCancellationRequested)
            {
                OperationCts.Cancel();
            }

            OperationCts.Dispose();
        }

        OperationCts = new CancellationTokenSource();
    }

    private async SyncTask<bool> AsyncStart(CancellationToken token)
    {
        if (!GameHandler.IsInGameCondition())
        {
            Logging.Logging.LogMessage("Not in game, operation will be terminated.", LogMessageType.Error);

            return false;
        }

        if (Settings.NonUserData.SelectedCraftingStepInputs.Count == 0 || Settings.NonUserData.SelectedCraftingStepInputs.Any(x => x.CheckType == ConditionalCheckType.ModifyThenCheck && string.IsNullOrEmpty(x.CurrencyItem)))
        {
            Logging.Logging.LogMessage("No Crafting Steps or currency to use is null, operation will be terminated.", LogMessageType.Error);

            return false;
        }

        if (SelectedCraftingSteps.Count == 0)
        {
            Logging.Logging.LogMessage("No valid crafting steps were built (possibly due to ItemFilter compilation errors), operation will be terminated.", LogMessageType.Error);

            return false;
        }

        try
        {
            Logging.Logging.LogMessage("Beginning inventory and stash handling.", LogMessageType.Debug);
            var isInvOpen = await InventoryHandler.AsyncWaitForInventoryOpen(token);
            var isStashOpen = await StashHandler.AsyncWaitForStashOpen(token);

            if (!isStashOpen || !isInvOpen)
            {
                Logging.Logging.LogMessage("Inventory or Stash could not be opened.", LogMessageType.Warning);

                return false;
            }

            Logging.Logging.LogMessage("Executing crafting sequence.", LogMessageType.Debug);
            var craftingSequenceExecutor = new CraftingSequenceExecutor(SelectedCraftingSteps);

            if (!await craftingSequenceExecutor.Execute(OperationCts.Token))
            {
                Logging.Logging.LogMessage("Crafting sequence execution failed.", LogMessageType.Error);
                return false;
            }

            Logging.Logging.LogMessage("AsyncStart() Completed.", LogMessageType.Info);
        }
        catch (OperationCanceledException)
        {
            Logging.Logging.LogMessage("Operation was canceled.", LogMessageType.Warning);
            Stop();
            return false;
        }

        Logging.Logging.LogMessage("AsyncStart() method completed successfully.", LogMessageType.Info);
        Logging.Logging.LogEndCraftingStats();

        if (Settings.Debugging.AutoFullLogDumpOnEnd)
        {
            Logging.Logging.SaveLog(Logging.Logging.CombineLogsToString(true));
        }
        return true;
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        ImGui.Separator();
        CraftingSequenceMenu.Draw();
    }
}