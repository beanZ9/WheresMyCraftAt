using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static WheresMyCraftAt.WheresMyCraftAt;

namespace WheresMyCraftAt.Handlers;

public static class StashHandler
{
    public static async SyncTask<bool> AsyncWaitForStashOpen(CancellationToken token, int timeout = 2) =>
        await ExecuteHandler.AsyncExecuteWithCancellationHandling(IsStashPanelOpenCondition, timeout, token);

    public static InventoryType GetTypeOfCurrentVisibleStash() =>
        Main.GameController?.Game?.IngameState.IngameUi?.StashElement?.VisibleStash?.InvType ?? InventoryType.InvalidInventory;

    public static IList<NormalInventoryItem> GetVisibleStashInventory() =>
        Main.GameController?.Game?.IngameState.IngameUi?.StashElement?.VisibleStash?.VisibleInventoryItems;

    public static bool IsStashPanelOpenCondition()
    {
        return ElementHandler.IsInGameUiElementVisibleCondition(ui => ui.StashElement);
    }

    public static bool IsVisibleStashValidCondition() =>
        GetTypeOfCurrentVisibleStash() != InventoryType.InvalidInventory;

    public static bool TryGetItemInStash(string baseName, out NormalInventoryItem foundItem)
    {
        foundItem = null;
        Logging.Logging.LogMessage($"Trying to find item '{baseName}' in stash.", Enums.WheresMyCraftAt.LogMessageType.Info);

        if (TryGetVisibleStashInventory(out var stashContents))
        {
            Logging.Logging.LogMessage($"Items in stash: {stashContents.Count}", Enums.WheresMyCraftAt.LogMessageType.Info);
            foundItem = stashContents.FirstOrDefault(item => ItemHandler.GetBaseNameFromItem(item)?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (foundItem == null)
        {
            Logging.Logging.LogMessage($"Could not find '{baseName}' in stash.", Enums.WheresMyCraftAt.LogMessageType.Warning);
        }
        else if (foundItem.Height < 1 || foundItem.Width < 1)
        {
            Logging.Logging.LogMessage($"Found '{baseName}' [W:{foundItem.Width}, H:{foundItem.Height}] in stash but this is an incorrect size.", Enums.WheresMyCraftAt.LogMessageType.Warning);
            return false;
        }
        else
        {
            Logging.Logging.LogMessage($"Found '{baseName}' [W:{foundItem.Width}, H:{foundItem.Height}] in stash.", Enums.WheresMyCraftAt.LogMessageType.Info);
        }

        return foundItem != null;
    }

    public static async SyncTask<Tuple<bool, NormalInventoryItem>> AsyncTryGetItemInStash(string currencyName, CancellationToken token)
    {
        NormalInventoryItem orbItem = null;

        Logging.Logging.LogMessage($"Attempting to find item '{currencyName}' in stash.", Enums.WheresMyCraftAt.LogMessageType.Info);

        var result = await ExecuteHandler.AsyncExecuteWithCancellationHandling(() => TryGetItemInStash(currencyName, out orbItem), 2, Main.ServerLatency, token);

        Logging.Logging.LogMessage($"Item '{currencyName}' found status: {result}.", Enums.WheresMyCraftAt.LogMessageType.Info);

        return Tuple.Create(result, orbItem);
    }

    public static async SyncTask<Tuple<bool, NormalInventoryItem>> AsyncTryGetStashSpecialSlot(Enums.WheresMyCraftAt.SpecialSlot slotType, CancellationToken token)
    {
        NormalInventoryItem inventoryItem = null;

        Logging.Logging.LogMessage($"Attempting to find special slot '{slotType}' in stash.", Enums.WheresMyCraftAt.LogMessageType.Info);

        var result = await ExecuteHandler.AsyncExecuteWithCancellationHandling(() => TryGetStashSpecialSlot(slotType, out inventoryItem),
            2,
            HelperHandler.GetRandomTimeInRange(Main.Settings.DelayOptions.MinMaxRandomDelayMS),
            token);

        switch (result)
        {
            case false:
                Logging.Logging.LogMessage($"AsyncTryGetStashSpecialSlot: Special slot '{slotType}' found status: {result}.", Enums.WheresMyCraftAt.LogMessageType.Error);
                Main.Stop();
                return Tuple.Create(result, inventoryItem);

            default:
                Logging.Logging.LogMessage($"AsyncTryGetStashSpecialSlot: Special slot '{slotType}' found status: {result}.", Enums.WheresMyCraftAt.LogMessageType.Info);
                return Tuple.Create(result, inventoryItem);
        }
    }

    public static bool TryGetStashSpecialSlot(Enums.WheresMyCraftAt.SpecialSlot slotType, out NormalInventoryItem inventoryItem)
    {
        inventoryItem = TryGetVisibleStashInventory(out var stashContents)
            ? stashContents.FirstOrDefault(item =>
                item.Elem.Size == Main.SpecialSlotDimensionMap[slotType]
                && item.IsValid
                && item.Item.IsValid
                && item.Item.TryGetComponent<Base>(out var baseComp)
                && baseComp.Address != 0
                && item.Item.TryGetComponent<Base>(out var modComp)
                && modComp.Address != 0)
            : null;

        return inventoryItem != null;
    }

    public static bool TryGetVisibleStashInventory(out IList<NormalInventoryItem> inventoryItems)
    {
        inventoryItems = IsVisibleStashValidCondition()
            ? GetVisibleStashInventory()
            : null;

        if (inventoryItems == null)
        {
            Logging.Logging.LogMessage("Visible stash inventory not found.", Enums.WheresMyCraftAt.LogMessageType.Warning);
        }
        else
        {
            Logging.Logging.LogMessage("Visible stash inventory retrieved.", Enums.WheresMyCraftAt.LogMessageType.Info);
        }

        return inventoryItems != null;
    }
}