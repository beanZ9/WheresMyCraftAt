using ExileCore.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WheresMyCraftAt.Handlers;
using static WheresMyCraftAt.CraftingSequence.CraftingSequence;
using static WheresMyCraftAt.Enums.WheresMyCraftAt;
using static WheresMyCraftAt.WheresMyCraftAt;

namespace WheresMyCraftAt.CraftingSequence;

public class CraftingSequenceExecutor(IEnumerable<CraftingBase> itemsToSequence)
{
    public async SyncTask<bool> Execute(CancellationToken token)
    {
        var readFromInventory = Main.Settings.RunOptions.CraftInventoryInsteadOfCurrencyTab;
        foreach (var craftingBase in itemsToSequence)
        {
            var currentStepIndex = 0;
            CraftingStep previousStep = null;
            var stopwatch = new Stopwatch(); // Stopwatch to time each step
            var lastItemAddress = long.MinValue; // Add comparison to address somehow being the same as the last sequence, issue if there is.
            var endCraft = false;
            var catchError = false;

            // Log initial item
            var asyncResult = readFromInventory
                ? new AsyncResult(await craftingBase.MethodReadInventoryItem(token))
                : new AsyncResult(await craftingBase.MethodReadStashItem(token));

            if (asyncResult.IsSuccess)
            {
                Logging.Logging.LogMessage("## CraftingSequenceExecutor: Starting Item", LogMessageType.ItemData);
                ItemHandler.PrintHumanModListFromItem(asyncResult.Entity);
            }
            else
            {
                Logging.Logging.LogMessage($"CraftingSequenceStep: Couldn't get StashSpecialSlot", LogMessageType.Error);
                Main.Stop();
            }

            while (!endCraft && !catchError && currentStepIndex >= 0 && currentStepIndex < craftingBase.CraftingSteps.Count)
            {
                var currentStep = craftingBase.CraftingSteps[currentStepIndex];
                var success = false; // Defaulting success to false
                var selectedBranchIndex = -1;

                try
                {
                    // Log item mods before each step
                    asyncResult = readFromInventory
                        ? new AsyncResult(await craftingBase.MethodReadInventoryItem(token))
                        : new AsyncResult(await craftingBase.MethodReadStashItem(token));

                    if (asyncResult.IsSuccess)
                    {
                        var currentItemAddress = asyncResult.Address;
                        if (lastItemAddress != long.MinValue)
                        {
                            if (previousStep is { CheckType: not ConditionalCheckType.ConditionalCheckOnly })
                            {
                                if (lastItemAddress == currentItemAddress)
                                {
                                    Logging.Logging.LogMessage($"CraftingSequenceStep: Item Address is the same as the last", LogMessageType.Special);
                                    Logging.Logging.LogMessage($"CraftingSequenceStep: (True) LastAddress[{lastItemAddress:X}], CurrentAddress[{currentItemAddress:X}].", LogMessageType.Special);
                                }
                                else
                                {
                                    Logging.Logging.LogMessage($"CraftingSequenceStep: Item Address is not the same as the last", LogMessageType.Special);
                                    Logging.Logging.LogMessage($"CraftingSequenceStep: (False) LastAddress[{lastItemAddress:X}], CurrentAddress[{currentItemAddress:X}].", LogMessageType.Special);
                                }

                                ItemHandler.PrintHumanModListFromItem(asyncResult.Entity);
                            }
                        }

                        lastItemAddress = currentItemAddress;
                    }
                    else
                    {
                        Logging.Logging.LogMessage($"CraftingSequenceStep: Couldn't get StashSpecialSlot", LogMessageType.Error);
                        Main.Stop();
                    }

                    // Info: Starting a new step
                    Logging.Logging.LogMessage($"CraftingSequenceStep: Executing step [{currentStepIndex + 1}]", LogMessageType.Special);

                    stopwatch.Restart(); // Start timing
                    if (currentStep.CheckType == ConditionalCheckType.ModifyThenCheck)
                    {
                        // Execute the method if no prior conditional check or if it's not applicable
                        var methodResult = await currentStep.Method(token);

                        Logging.Logging.LogMessage($"CraftingSequenceStep: Method result is {methodResult}", LogMessageType.Special);
                    }

                    if (currentStep.AutomaticSuccess)
                    {
                        success = true; // Override success if AutomaticSuccess is true
                        Logging.Logging.LogMessage($"CraftingSequenceStep: AutomaticSuccess is {success}", LogMessageType.Special);
                    }
                    else if (currentStep.CheckType == ConditionalCheckType.Branch)
                    {
                        for (int branchIndex = 0; branchIndex < currentStep.Branches.Count; branchIndex++)
                        {
                            if (await EvaluateConditionsAsync(currentStep.Branches[branchIndex].ConditionalGroups, token))
                            {
                                Logging.Logging.LogMessage($"CraftingSequenceStep: Selected branch {branchIndex}", LogMessageType.Special);
                                selectedBranchIndex = branchIndex;
                                break;
                            }
                        }

                        success = selectedBranchIndex != -1;
                    }
                    else if (currentStep.ConditionalCheckGroups.Count != 0)
                    {
                        // Count how many conditions are true and check if it meets or exceeds the required count
                        success = await EvaluateConditionsAsync(currentStep, token);

                        Logging.Logging.LogMessage($"CraftingSequenceStep: ConditionalCheck result for {currentStep.CheckType} is {success}", LogMessageType.Special);
                    }
                    else
                    {
                        // somehow 0 conditions equals automatic failure??
                    }

                    stopwatch.Stop(); // Stop timing after the step is executed

                    // Profiler: Time taken for step execution
                    Logging.Logging.LogMessage($"CraftingSequenceStep: Step [{currentStepIndex + 1}] completed in {stopwatch.ElapsedMilliseconds} ms", LogMessageType.Profiler);
                    previousStep = currentStep;
                }
                catch (Exception ex)
                {
                    Logging.Logging.LogMessage($"CraftingSequenceExecutor: Exception caught while executing step {currentStepIndex + 1}:\n{ex}", LogMessageType.Error);

                    Logging.Logging.LogMessage($"CraftingSequenceStep: Step [{currentStepIndex + 1}] failed after {stopwatch.ElapsedMilliseconds} ms", LogMessageType.Profiler);
                    Logging.Logging.LogMessage($"CraftingSequenceExecutor: Error Item (name:{ItemHandler.GetBaseNameFromItem(asyncResult.Entity)}, adr:{asyncResult.Address:X})", LogMessageType.Error);

                    Logging.Logging.LogMessage($"CraftingSequenceExecutor: Skipping onto the next item if there is one.", LogMessageType.Error);
                    catchError = true;
                    continue;
                }

                // Determine the next step based on success or failure
                UpdateOperationStepsDictionary(currentStepIndex, success);

                var action = currentStep.CheckType == ConditionalCheckType.Branch
                    ? selectedBranchIndex != -1
                        ? currentStep.Branches[selectedBranchIndex].MatchAction
                        : AnyAction.End
                    : success
                        ? currentStep.SuccessAction
                        : currentStep.FailureAction;

                Logging.Logging.LogMessage($"CraftingSequenceStep: Sequence result {action}", LogMessageType.Special);

                switch (action)
                {
                    case AnyAction.Continue:
                        currentStepIndex++;
                        break;
                    case AnyAction.End:
                        endCraft = true;
                        break; // End the execution of the sequence
                    case AnyAction.GoToStep:
                        currentStepIndex = currentStep.CheckType == ConditionalCheckType.Branch
                            ? currentStep.Branches[selectedBranchIndex].MatchActionStepIndex
                            : success
                                ? currentStep.SuccessActionStepIndex
                                : currentStep.FailureActionStepIndex;
                        break;
                    case AnyAction.RepeatStep:
                        // Stay on the current step
                        break;
                    case AnyAction.Restart:
                        currentStepIndex = 0; // Restart from the first step
                        break;
                }

                // Info: Next step to be executed
                Logging.Logging.LogMessage(currentStepIndex < craftingBase.CraftingSteps.Count - 1 // Check if it's not the last step
                    ? $"CraftingSequenceStep: Next step is [{currentStepIndex + 1}]"
                    // If it's the last step, you might want to log a different message or nothing at all
                    : "CraftingSequenceStep: Reached the last step in the sequence.", LogMessageType.Special);
            }

            if (!catchError)
            {
                // Log item mods at the end of crafting
                asyncResult = readFromInventory
                    ? new AsyncResult(await craftingBase.MethodReadInventoryItem(token))
                    : new AsyncResult(await craftingBase.MethodReadStashItem(token));

                if (asyncResult.IsSuccess)
                {
                    Logging.Logging.LogMessage("## CraftingSequenceExecutor: End Item", LogMessageType.ItemData);
                    ItemHandler.PrintHumanModListFromItem(asyncResult.Entity);
                    if (readFromInventory)
                    {
                        Main.CompletedCrafts[(int)craftingBase.CraftingPosition.Y, (int)craftingBase.CraftingPosition.X] = 1;
                    }
                }
            }
        }
        

        // Info: Sequence completed successfully
        Logging.Logging.LogMessage("CraftingSequenceExecutor: Sequence execution completed successfully.", LogMessageType.Special);

        return true;
        
        static void UpdateOperationStepsDictionary(int step, bool pass)
        {
            Main.CurrentOperationStepCountList ??= [];

            if (Main.CurrentOperationStepCountList.TryGetValue(step, out var currentCount))
            {
                if (pass)
                {
                    Main.CurrentOperationStepCountList[step] = (currentCount.passCount + 1, currentCount.failCount, currentCount.totalCount + 1);
                }
                else
                {
                    Main.CurrentOperationStepCountList[step] = (currentCount.passCount, currentCount.failCount + 1, currentCount.totalCount + 1);
                }
            }
            else
            {
                Main.CurrentOperationStepCountList[step] = pass ? (1, 0, 1) : (0, 1, 1);
            }
        }
    }

    private static async SyncTask<bool> EvaluateConditionsAsync(CraftingStep currentStep, CancellationToken token)
    {
        return await EvaluateConditionsAsync(currentStep.ConditionalCheckGroups, token);
    }

    private static async SyncTask<bool> EvaluateConditionsAsync(List<ConditionalChecksGroup> groups, CancellationToken cancellationToken)
    {
        var hasOrGroup = false;
        var orResult = false; // Start false for OR logic

        foreach (var group in groups)
        {
            var trueCount = await CountTrueAsync(group.ConditionalChecks, cancellationToken);

            if (!trueCount.result)
            {
                Logging.Logging.LogMessage("EvaluateConditionsAsync: At some point we couldn't find our item in the slot wanted, stopping", LogMessageType.Error);

                Main.Stop();
            }

            switch (group.GroupType)
            {
                case ConditionGroup.AND:
                    var andResult = trueCount.trueCount >= group.ConditionalsToBePassForSuccess;

                    Logging.Logging.LogMessage($"AND Group Result: {andResult} (True Count: {trueCount.trueCount}, Required: {group.ConditionalsToBePassForSuccess})", LogMessageType.Evaluation);

                    if (!andResult)
                    {
                        Logging.Logging.LogMessage("Exiting early due to AND group result being false", LogMessageType.Evaluation);
                        return false;
                    }

                    break;
                case ConditionGroup.OR:
                    hasOrGroup = true;
                    orResult |= trueCount.trueCount >= group.ConditionalsToBePassForSuccess;

                    Logging.Logging.LogMessage($"OR Group Result: {orResult} (True Count: {trueCount.trueCount}, Required: {group.ConditionalsToBePassForSuccess})", LogMessageType.Evaluation);

                    break;
                case ConditionGroup.NOT:
                    if (trueCount.trueCount > 0)
                    {
                        Logging.Logging.LogMessage("NOT Group Result: False (At least one condition is true)", LogMessageType.Evaluation);

                        return false;
                    }

                    Logging.Logging.LogMessage("NOT Group Result: True (No conditions are true)", LogMessageType.Evaluation);

                    break;
            }
        }

        var combinedResult = !hasOrGroup || orResult;
        Logging.Logging.LogMessage($"Final Combined Result: {combinedResult}", LogMessageType.Evaluation);
        return combinedResult;
    }

    static async SyncTask<(bool result, int trueCount)> CountTrueAsync(IEnumerable<Func<CancellationToken, SyncTask<(bool result, bool isMatch)>>> conditionalChecks,
        CancellationToken token)
    {
        var trueCount = 0;
        var allSuccessful = true;

        foreach (var condition in conditionalChecks)
        {
            var (result, isMatch) = await condition(token);

            if (!result)
            {
                allSuccessful = false;
                break;
            }

            if (isMatch)
            {
                trueCount++;
            }
        }

        return (allSuccessful, trueCount);
    }
}