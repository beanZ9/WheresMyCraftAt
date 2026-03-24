using ExileCore.Shared.Attributes;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ItemFilterLibrary;
using WheresMyCraftAt.CraftingMenu;
using WheresMyCraftAt.CraftingSequence;
using WheresMyCraftAt.Handlers;
using Vector2 = System.Numerics.Vector2;

namespace WheresMyCraftAt;

public class WheresMyCraftAtSettings : ISettings
{
    public WheresMyCraftAtSettings()
    {
        RunOptions = new RunOptions(Styling);
    }

    public RunOptions RunOptions { get; set; }
    public DelayOptions DelayOptions { get; set; } = new();
    public DebugOptions Debugging { get; set; } = new();
    public StylingDooDads Styling { get; set; } = new();
    public NonUser NonUserData { get; set; } = new();
    public ToggleNode Enable { get; set; } = new(false);
}

[Submenu(CollapsedByDefault = false)]
public class RunOptions
{
    public HotkeyNode RunButton { get; set; } = Keys.NumPad6;
    public ToggleNode CraftInventoryInsteadOfCurrencyTab { get; set; } = new(false);

    [ConditionalDisplay(nameof(CraftInventoryInsteadOfCurrencyTab))]
    public TextNode InventoryFilter { get; set; } = new("");

    [ConditionalDisplay(nameof(CraftInventoryInsteadOfCurrencyTab))]
    public ToggleNode ApplyFilter { get; set; } = new(false);

    [ConditionalDisplay(nameof(CraftInventoryInsteadOfCurrencyTab))]
    public ButtonNode ToggleAll { get; set; } = new();

    [JsonIgnore]
    [ConditionalDisplay(nameof(CraftInventoryInsteadOfCurrencyTab))]
    public CustomNode InventorySectionSelector { get; }
    public int[,] InventoryCraftingSlots { get; set; } = new int[5, 12];

    public RunOptions(StylingDooDads Styling)
    {
        ToggleAll.OnPressed = () =>
        {
            var anyToggled = false;
            for (int row = 0; row < 5 && !anyToggled; row++)
            {
                for (int column = 0; column < 12; column++)
                {
                    if (InventoryCraftingSlots[row, column] == 1)
                    {
                        anyToggled = true;
                        break;
                    }
                }
            }

            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 12; column++)
                {
                    InventoryCraftingSlots[row, column] = anyToggled ? 0 : 1;
                }
            }
        };

        InventorySectionSelector = new CustomNode
        {
            DrawDelegate = () =>
            {
                ImGui.Separator();
                ImGui.TextWrapped("Select the top left slot each item occupies in the inventory you want crafted on.\nI highly advise Styling be enabled to visually see what slots are considered valid positions otherwise you will only get a tooltip when it is hovered.");

                var itemsInInventory = InventoryHandler.TryGetValidCraftingItemsFromAnInventory(InventorySlotE.MainInventory1).ToList();
                ItemFilter filter = null;
                if (ApplyFilter)
                {
                    ItemQuery query = null;
                    try
                    {
                        query = CraftingSequenceMenu.GetInventoryFilterQuery();
                    }
                    catch (Exception ex)
                    {
                        using (ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.Red.ToImgui()))
                        {
                            ImGui.TextWrapped($"Query failed to load: {ex.Message}");
                        }
                    }

                    if (query != null)
                    {
                        filter = new ItemFilter([query]);
                    }
                }
            

                var numb = 1;
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1, 1));
                for (var row = 0; row < 5; row++)
                {
                    for (var col = 0; col < 12; col++)
                    {
                        ImGui.PushID($"{numb}_cell");

                        var isValidItemInSlot = itemsInInventory.Any(item => item.PosX == col && item.PosY == row);
                        if (isValidItemInSlot && Styling.CustomMenuStyling)
                        {
                            var isFiltered = ApplyFilter &&
                                             filter != null &&
                                             InventoryHandler.TryGetInventoryItemFromSlot(new Vector2(col, row), out var item) &&
                                             FilterHandler.IsItemMatchingCondition(item.Item, filter);
                            ImGui.PushStyleColor(ImGuiCol.FrameBg,
                                isFiltered ? Styling.InventoryVisualizer.Filtered.Value.ToImgui() : Styling.InventoryVisualizer.BackgroundNormal.Value.ToImgui());
                            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Styling.InventoryVisualizer.BackgroundHovered.Value.ToImgui());
                            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Styling.InventoryVisualizer.BackgroundActive.Value.ToImgui());
                        }

                        var toggled = Convert.ToBoolean(InventoryCraftingSlots[row, col]);
                        if (ImGui.Checkbox("", ref toggled))
                        {
                            InventoryCraftingSlots[row, col] ^= 1;
                        }

                        if (isValidItemInSlot && ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text(
                                $"{(InventoryHandler.TryGetInventoryItemFromSlot(new Vector2(col, row), out var item) 
                                ? $"{ItemHandler.GetBaseNameFromItem(item)}\nX:{col}, Y:{row}" 
                                : "<error>")}");

                            ImGui.EndTooltip();
                        }

                        if ((numb - 1) % 12 < 11)
                        {
                            ImGui.SameLine();
                        }

                        numb++;
                        ImGui.PopID();
                        if (isValidItemInSlot && Styling.CustomMenuStyling)
                        {
                            ImGui.PopStyleColor(3);
                        }
                    }
                }

                ImGui.PopStyleVar(1);
            }
        };
    }
}

public class NonUser
{
    public string CraftingSequenceLastSaved { get; set; } = "";
    public string CraftingSequenceLastSelected { get; set; } = "";
    public List<CraftingSequence.CraftingSequence.CraftingStepInput> SelectedCraftingStepInputs { get; set; } = [];
}

[Submenu(CollapsedByDefault = true)]
public class DelayOptions
{
    public RangeNode<Vector2> MinMaxRandomDelayMS { get; set; } = new(
        new Vector2(5, 15),
        new Vector2(1, 2),
        new Vector2(999, 1000)
    );

    public RangeNode<int> AddressChangeDelayMS { get; set; } = new(20, 1, 150);

    public RangeNode<int> ActionTimeoutInSeconds { get; set; } = new(2, 1, 3);
}

[Submenu(CollapsedByDefault = true)]
public class DebugOptions
{
    public Dictionary<Enums.WheresMyCraftAt.LogMessageType, (bool enabled, Color color)> LogMessageFilters = [];

    public ToggleNode LogWindow { get; set; } = new(false);
    public HotkeyNode ToggleLogWindow { get; set; } = new(Keys.NumPad3);
    public ToggleNode PrintTopLeft { get; set; } = new(true);
    public RangeNode<int> PrintLingerTime { get; set; } = new(5, 0, 20);
    public ToggleNode AutoFullLogDumpOnEnd { get; set; } = new(true);

    [JsonIgnore]
    public ToggleNode InspectCraftedItem { get; set; } = new(false);
}

[Submenu(CollapsedByDefault = true)]
public class StylingDooDads
{
    public TextNode LogTimeFormat { get; set; } = new TextNode("HH:mm:ss.ffff tt");
    public ToggleNode CustomMenuStyling { get; set; } = new(true);
    public RemovalButtonStyle RemovalButtons { get; set; } = new();
    public AdditionButtonStyle AdditionButtons { get; set; } = new();
    public ConditionalGroupStyle ConditionGroupBackgrounds { get; set; } = new();
    public BranchGroupStyle BranchGroup { get; set; } = new();
    public InventoryVisualizerGroupStyle InventoryVisualizer { get; set; } = new();
}

[Submenu]
public class RemovalButtonStyle
{
    public ColorNode Normal { get; set; } = new(new Color(250, 66, 66, 102));
    public ColorNode Hovered { get; set; } = new(new Color(250, 66, 66, 255));
    public ColorNode Active { get; set; } = new(new Color(250, 15, 15, 255));
}

[Submenu]
public class AdditionButtonStyle
{
    public ColorNode Normal { get; set; } = new(new Color(66, 250, 66, 102));
    public ColorNode Hovered { get; set; } = new(new Color(66, 250, 66, 150));
    public ColorNode Active { get; set; } = new(new Color(15, 250, 15, 200));
}

[Submenu]
public class ConditionalGroupStyle
{
    public ColorNode And { get; set; } = new(new Color(76, 209, 65, 18));
    public ColorNode Or { get; set; } = new(new Color(76, 100, 209, 30));
    public ColorNode Not { get; set; } = new(new Color(209, 76, 65, 18));
}

[Submenu]
public class InventoryVisualizerGroupStyle
{
    public ColorNode BackgroundNormal { get; set; } = new(new Color(209, 209, 209, 60));
    public ColorNode BackgroundHovered { get; set; } = new(new Color(152, 128, 34, 178));
    public ColorNode BackgroundActive { get; set; } = new(new Color(152, 128, 34, 240));
    public ColorNode Filtered { get; set; } = new(Color.Pink);
}

[Submenu]
public class BranchGroupStyle
{
    public ColorNode Background { get; set; } = new(new Color(231, 255, 43, 18));
}