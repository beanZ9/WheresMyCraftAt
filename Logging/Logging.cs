using ExileCore;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using WheresMyCraftAt.CraftingMenu;
using WheresMyCraftAt.Handlers;
using static WheresMyCraftAt.Enums.WheresMyCraftAt;
using static WheresMyCraftAt.WheresMyCraftAt;

namespace WheresMyCraftAt.Logging;

public static class Logging
{
    private static readonly object Locker = new();
    public static List<DebugMsgDescription> MessagesList = [];

    public static void Render()
    {
        using var fontPush = Main.Graphics.UseCurrentFont();
        var flags = ImGuiWindowFlags.None;

        if (Main.CurrentOperation is not null)
        {
            flags = ImGuiWindowFlags.NoInputs;
        }

        var isOpen = Main.Settings.Debugging.LogWindow.Value;

        if (isOpen)
        {
            ImGui.Begin("Wheres My Craft At Logs", ref isOpen, flags);
            var logMessageTypes = Main.Settings.Debugging.LogMessageFilters.Keys.ToList();

            var Label = "Configure Logging Filters And Their Colors";
            var startPos = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(startPos - 20f);
            if (ImGui.TreeNodeEx($"##{Label}", ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(startPos);
                ImGui.SeparatorText(Label);
                foreach (var logMessageType in logMessageTypes)
                {
                    var logSetting = Main.Settings.Debugging.LogMessageFilters[logMessageType];
                    var colorToVector4 = logSetting.color.ToImguiVec4();

                    ImGui.ColorEdit4($"##ColorPicker_{logMessageType}", ref colorToVector4, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar);

                    logSetting.color = colorToVector4.ToSharpColor();
                    ImGui.SameLine();
                    ImGui.Checkbox(logMessageType.ToString(), ref logSetting.enabled);
                    Main.Settings.Debugging.LogMessageFilters[logMessageType] = logSetting;
                }

                ImGui.TreePop();
            }
            else
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(startPos);
                ImGui.SeparatorText(Label);
            }

            if (ImGui.Button("Save Toggled Log"))
            {
                SaveLog(CombineLogsToString(false));
            }

            ImGui.SameLine();

            if (ImGui.Button("Save All Log"))
            {
                SaveLog(CombineLogsToString(true));
            }

            ImGui.SameLine();

            if (ImGui.Button("Open Log Folder"))
            {
                var fullPath = Path.Combine(Main.ConfigDirectory, "SavedLogs");

                if (!Directory.Exists(fullPath))
                {
                    LogMessage("Unable to open log directory because it does not exist.", LogMessageType.Error);
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = fullPath
                    });

                    LogMessage("Opened log directory in explorer.", LogMessageType.Info);
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Clear"))
            {
                lock (Locker)
                {
                    MessagesList.Clear();
                }
            }

            ImGui.BeginChild("LogMessages", new Vector2(0, 0), ImGuiChildFlags.Border, ImGuiWindowFlags.AlwaysHorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);

            lock (Locker)
            {
                foreach (var msg in MessagesList.Where(msg => msg != null))
                {
                    var (enabled, color) = Main.Settings.Debugging.LogMessageFilters[msg.LogType];

                    if (!enabled)
                    {
                        continue;
                    }

                    ImGui.PushStyleColor(ImGuiCol.Text, color.ToImguiVec4());
                    ImGui.TextUnformatted($"{msg.Time.ToString(Main.Settings.Styling.LogTimeFormat.Value)}: {msg.Msg}");
                    ImGui.PopStyleColor();
                }
            }

            // Keep up at the bottom of the scroll region if we were already at the bottom at the beginning of the frame.
            // Using a scrollbar or mouse-wheel will take away from the bottom edge.
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }

            ImGui.EndChild();
            ImGui.End();
        }

        Main.Settings.Debugging.LogWindow.Value = isOpen;
    }

    public static void LogMessage(string msg, LogMessageType messageType)
    {
        if (msg == null)
        {
            DebugWindow.LogError("LogMessage error: msg is null");
            return;
        }

        try
        {
            var color = Main.Settings.Debugging.LogMessageFilters[messageType].color;

            if (Main.Settings.Debugging.PrintTopLeft || 
                messageType is LogMessageType.Critical or LogMessageType.Warning or LogMessageType.Error)
            {
                DebugWindow.LogMsg(msg, Main.Settings.Debugging.PrintLingerTime, color);
            }

            var debugMsgDescription = new DebugMsgDescription
            {
                Msg = msg,
                Time = DateTime.Now,
                LogType = messageType
            };

            lock (Locker)
            {
                MessagesList.Add(debugMsgDescription);
            }
        }
        catch (Exception e)
        {
            DebugWindow.LogError($"{nameof(Logging)} -> {e}");
        }
    }

    public static void SaveLog(List<string> input)
    {
        try
        {
            var lastSelected = Main.Settings.NonUserData.CraftingSequenceLastSelected;
            var fullPath = Path.Combine(Main.ConfigDirectory,
                "SavedLogs",
                !string.IsNullOrEmpty(lastSelected)
                    ? HelperHandler.CleanWindowsString(lastSelected)
                    : "Unknown");

            Directory.CreateDirectory(fullPath);
            var filename = $"{DateTime.Now:dd-MM-yyyy__HH-mm-ss}__{(Main.Settings.RunOptions.CraftInventoryInsteadOfCurrencyTab ? "Inventory" : "Stash")}.txt";
            var fullFilePath = Path.Combine(fullPath, filename);
            File.WriteAllLines(fullFilePath, input);
            LogMessage($"Successfully saved file to {fullFilePath}.", LogMessageType.Info);
        }
        catch (Exception e)
        {
            var lastSelected = Main.Settings.NonUserData.CraftingSequenceLastSelected;
            var errorPath = Path.Combine(Main.ConfigDirectory,
                "SavedLogs",
                !string.IsNullOrEmpty(lastSelected)
                    ? HelperHandler.CleanWindowsString(lastSelected)
                    : "Unknown");

            LogMessage($"Error saving file to {errorPath}: {e.Message}", LogMessageType.Error);
        }
    }

    public static void LogEndCraftingStats()
    {
        const LogMessageType messageType = LogMessageType.EndSessionStats;

        LogTotalItemsAppliedSuccessfully();
        LogTotalStepsRun();
        LogSuccessfulCraftSlots();

        AddSeparator();

        void AddSeparator()
        {
            LogMessage("-----------", messageType);
        }

        void LogTotalItemsAppliedSuccessfully()
        {
            AddSeparator();
            LogMessage("Total Items Applied Successfully:", messageType);
            var maxItemNameLength = Main.CurrentOperationUsedItemsList.Keys.Select(k => k.Length).DefaultIfEmpty().Max();

            foreach (var itemUsed in Main.CurrentOperationUsedItemsList)
            {
                var paddedItemName = itemUsed.Key.PadRight(maxItemNameLength);
                LogMessage($"{paddedItemName}: {itemUsed.Value}", messageType);
            }
        }

        void LogTotalStepsRun()
        {
            AddSeparator();
            LogMessage("Total Steps Run:", messageType);

            if (Main.CurrentOperationStepCountList.Count == 0) return;
            var sortedStepCountList = Main.CurrentOperationStepCountList.OrderBy(x => x.Key).ToList();
            var maxLengths = CalculateMaxLengths(sortedStepCountList);

            foreach (var (key, stepCount) in sortedStepCountList)
            {
                var formattedStepDetails = FormatStepDetails(key, stepCount, maxLengths);
                LogMessage(formattedStepDetails, messageType);
            }
        }

        (int maxTitleLength, int maxPassLength, int maxFailLength, int maxTotalLength) CalculateMaxLengths(
            IEnumerable<KeyValuePair<int, (int passCount, int failCount, int totalCount)>> sortedStepCountList)
        {
            var maxTitleLength = sortedStepCountList.Max(s => $"STEP [{s.Key + 1}] ".Length + GetStepActionTitle(s.Key).Length);
            var maxPassLength = sortedStepCountList.Max(s => s.Value.passCount.ToString().Length);
            var maxFailLength = sortedStepCountList.Max(s => s.Value.failCount.ToString().Length);
            var maxTotalLength = sortedStepCountList.Max(s => s.Value.totalCount.ToString().Length);

            return (maxTitleLength, maxPassLength, maxFailLength, maxTotalLength);
        }

        string FormatStepDetails(int key, (int passCount, int failCount, int totalCount) stepCount, (int maxTitleLength, int maxPassLength, int maxFailLength, int maxTotalLength) maxLengths)
        {
            var (maxTitleLength, maxPassLength, maxFailLength, maxTotalLength) = maxLengths;

            var stepTitleLine = $"STEP [{key + 1}] {GetStepActionTitle(key)}".PadRight(maxTitleLength);
            var passLine = $": (pass:{stepCount.passCount.ToString().PadLeft(maxPassLength)}, ";
            var failLine = $"fail:{stepCount.failCount.ToString().PadLeft(maxFailLength)}, ";
            var totalLine = $"total:{stepCount.totalCount.ToString().PadLeft(maxTotalLength)})";

            return stepTitleLine + passLine + failLine + totalLine;
        }

        void LogSuccessfulCraftSlots()
        {
            if (!Main.Settings.RunOptions.CraftInventoryInsteadOfCurrencyTab) return;

            AddSeparator();
            LogMessage("Successful Craft Slots", messageType);

            for (var row = 0; row < 5; row++)
            {
                var text = "";
                for (var col = 0; col < 12; col++)
                {
                    text += Main.CompletedCrafts[row, col] == 1
                        ? "[X]"
                        : "[ ]";

                    if (col < 11) text += ",";
                }

                LogMessage(text, messageType);
            }
        }

        string GetStepActionTitle(int key)
        {
            var inputs = Main.Settings.NonUserData.SelectedCraftingStepInputs[key];

            return CraftingSequenceMenu.GetStepText(inputs);
        }
    }

    public static List<string> CombineLogsToString(bool all)
    {
        lock (Locker)
        {
            if (all)
            {
                return MessagesList.Where(msg => msg != null)
                    .Select(msg => $"{msg.Time.ToString(Main.Settings.Styling.LogTimeFormat.Value)}: {msg.Msg}")
                    .ToList();
            }
            return MessagesList.Where(msg => msg != null && Main.Settings.Debugging.LogMessageFilters.ContainsKey(msg.LogType) && Main.Settings.Debugging.LogMessageFilters[msg.LogType].enabled)
                .Select(msg => $"{msg.Time.ToString(Main.Settings.Styling.LogTimeFormat.Value)}: {msg.Msg}")
                .ToList();
        }
    }

    public class DebugMsgDescription
    {
        public string Msg { get; init; }
        public DateTime Time { get; init; }
        public LogMessageType LogType { get; init; }
    }
}