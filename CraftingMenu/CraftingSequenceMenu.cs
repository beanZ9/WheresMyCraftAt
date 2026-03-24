using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ImGuiNET;
using ItemFilterLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using ExileCore.Shared.Cache;
using WheresMyCraftAt.CraftingMenu.CraftofExileStructs;
using WheresMyCraftAt.CraftingMenu.Styling;
using WheresMyCraftAt.Extensions;
using WheresMyCraftAt.Handlers;
using static WheresMyCraftAt.CraftingSequence.CraftingSequence;
using static WheresMyCraftAt.Enums.WheresMyCraftAt;
using static WheresMyCraftAt.WheresMyCraftAt;
using Vector2 = System.Numerics.Vector2;
using WheresMyCraftAt.CraftingSequence;
namespace WheresMyCraftAt.CraftingMenu;

public static class CraftingSequenceMenu
{
    private const string DeletePopup = "Delete Confirmation";
    private const string OverwritePopup = "Overwrite Confirmation";

    private static EditorRecord Editor = new EditorRecord(-1, -1, -1, -1);

    private static string _fileSaveName = Main.Settings.NonUserData.CraftingSequenceLastSaved;
    private static string _selectedFileName = Main.Settings.NonUserData.CraftingSequenceLastSaved;

    private static List<string> _files = [];
    private static string tempCondValue = string.Empty;
    private static string condEditValue = string.Empty;

    private static CoELang _coELang;
    private static JObject _coeData;
    private static string _coeImportData = string.Empty;

    private static readonly Dictionary<string, (bool isCompiled, string errorMessage)> _filterCompilationCache
        = new Dictionary<string, (bool isCompiled, string errorMessage)>();

    private static ColorButton RemovalButton =>
        new ColorButton(Main.Settings.Styling.RemovalButtons.Normal, Main.Settings.Styling.RemovalButtons.Hovered, Main.Settings.Styling.RemovalButtons.Active);

    private static ColorButton AdditionButton =>
        new ColorButton(
            Main.Settings.Styling.AdditionButtons.Normal, Main.Settings.Styling.AdditionButtons.Hovered, Main.Settings.Styling.AdditionButtons.Active);

    public static void Draw()
    {
        if (Main == null)
            return;

        if (!Main.Settings.Enable)
            ResetEditingIdentifiers();

        DrawFileOptions();
        DrawImportSettings();

        if (Main.Settings.NonUserData.SelectedCraftingStepInputs.Count <= 0)
            Main.Settings.NonUserData.SelectedCraftingStepInputs.Add(new CraftingStepInput());

        DrawConfirmAndClear();
        DrawInstructions();
        DrawCraftingStepInputs();
    }

    private static int GetEnumLength<T>() => Enum.GetNames(typeof(T)).Length;

    private static void DrawCraftingStepInputs()
    {
        var currentSteps = new List<CraftingStepInput>(Main.Settings.NonUserData.SelectedCraftingStepInputs);

        for (var stepIndex = 0; stepIndex < currentSteps.Count; stepIndex++)
        {
            ImGui.PushID("Step_" + stepIndex);

            var dropTargetStart = ImGui.GetCursorPos();

            ImGui.Button("=");

            if (ImGui.BeginDragDropSource())
            {
                ImGuiHelpers.SetDragDropPayload("StepIndex", stepIndex);
                var headerText = $"STEP [{stepIndex + 1}] - ";
                var nextStep = currentSteps[stepIndex];
                headerText += GetStepText(nextStep);

                ImGui.Text($"Dragging Step '{headerText}'");
                ImGui.EndDragDropSource();
            }

            ImGui.PushStyleColor(ImGuiCol.Button, 0);

            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Drag me");

            ImGui.SameLine();

            DrawSingleCraftingStep(currentSteps, ref stepIndex);

            if (ImGuiHelpers.DrawAllColumnsBox("##DragTarget", dropTargetStart) && ImGui.BeginDragDropTarget())
            {
                var payload = ImGuiHelpers.AcceptDragDropPayload<int>("StepIndex");
                if (payload != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    var sourceIndex = payload.Value;
                    if (sourceIndex != stepIndex)
                    {
                        var draggedStep = currentSteps[sourceIndex];
                        currentSteps.RemoveAt(sourceIndex);
                        currentSteps.Insert(stepIndex, draggedStep);
                    }
                }

                ImGui.EndDragDropTarget();
            }

            ImGui.PopID();
        }

        Main.Settings.NonUserData.SelectedCraftingStepInputs = currentSteps;

        using (AdditionButton)
        {
            if (ImGui.Button("[=] Add New Step"))
            {
                ResetEditingIdentifiers();
                Main.Settings.NonUserData.SelectedCraftingStepInputs.Add(new CraftingStepInput());
            }
        }

        Main.Settings.NonUserData.CraftingSequenceLastSaved = _fileSaveName;
        Main.Settings.NonUserData.CraftingSequenceLastSelected = _selectedFileName;
    }

    public static string GetStepText(CraftingStepInput step)
    {
        return step.CheckType switch
        {
            ConditionalCheckType.ConditionalCheckOnly => "Check The Item",
            ConditionalCheckType.Branch => "Branch",
            ConditionalCheckType.ModifyThenCheck => $"Use '{step.CurrencyItem}'"
        };
    }

    private static void DrawSingleCraftingStep(List<CraftingStepInput> steps, ref int stepIndex)
    {
        ImGui.PushID(stepIndex);
        var currentStep = steps[stepIndex];
        var headerText = $"STEP [{stepIndex + 1}] - ";
        headerText += GetStepText(currentStep);

        if (ImGui.CollapsingHeader(headerText, ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.BeginChild("ConditionalGroup", Vector2.Zero, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);
            ImGui.Indent();

            DrawMethodTypeSection(currentStep);

            if (currentStep.CheckType == ConditionalCheckType.ModifyThenCheck)
                DrawCurrencyAndAutoSuccessSection(currentStep);
            else
                currentStep.AutomaticSuccess = false;

            if (currentStep.CheckType == ConditionalCheckType.ModifyThenCheck || currentStep.CheckType == ConditionalCheckType.ConditionalCheckOnly)
            {
                DrawSuccessActionSection(currentStep, steps, stepIndex);

                if (!currentStep.AutomaticSuccess)
                {
                    DrawFailureActionSection(currentStep, steps, stepIndex);
                    DrawCopyConditionalGroupsSection(currentStep, steps, stepIndex);
                    DrawAddConditionalGroupButton(currentStep);
                    DrawConditionalGroups(currentStep, steps, stepIndex);
                }
            }

            if (currentStep.CheckType == ConditionalCheckType.Branch)
            {
                currentStep.ConditionalGroups.Clear();
                currentStep.CurrencyItem = "";
                DrawBranchManager(steps, currentStep, stepIndex);
            }

            if (DrawStepManipulationControls(steps, ref stepIndex))
            {
                ImGui.Unindent();
                ImGui.EndChild();
                ImGui.PopID();
                return;
            }

            ImGui.Unindent();
            ImGui.EndChild();
        }

        ImGui.PopID();
    }

    private static void DrawBranchManager(List<CraftingStepInput> steps, CraftingStepInput currentStep, int stepIndex)
    {
        using (AdditionButton)
        {
            if (ImGui.Button("Add Branch"))
            {
                currentStep.Branches.Add(
                    new CraftingStepBranchInput
                    {
                        MatchAction = AnyAction.Continue,
                        MatchActionStepIndex = 1,
                        ConditionalGroups =
                        [
                            new ConditionalChecksGroupInput
                            {
                                ConditionalsToBePassForSuccess = 1,
                                GroupType = ConditionGroup.AND,
                                Conditionals =
                                [
                                    new ConditionalKeys
                                    {
                                        Name = "",
                                        Value = ""
                                    }
                                ]
                            }
                        ]
                    });
            }
        }

        using (new ColorBackground((ImGuiCol.ChildBg, Main.Settings.Styling.BranchGroup.Background.Value)))
        {
            ImGui.BeginChild("ConditionalGroup", Vector2.Zero, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);
            ImGui.Indent();
            for (var branchIndex = 0; branchIndex < currentStep.Branches.Count; branchIndex++)
            {
                var branch = currentStep.Branches[branchIndex];
                ImGui.PushID($"branch{branchIndex}");
                using (RemovalButton)
                {
                    if (ImGui.Button("Remove"))
                    {
                        ResetEditingIdentifiers();
                        currentStep.Branches.RemoveAt(branchIndex);
                        branchIndex--;
                        ImGui.PopID();
                        continue;
                    }
                }

                ImGui.SameLine();
                DrawBranchEditor(currentStep, branch, steps, stepIndex, branchIndex);
                ImGui.PopID();
            }

            ImGui.Unindent();
            ImGui.EndChild();
        }
    }

    private static void DrawMethodTypeSection(CraftingStepInput step)
    {
        var checkTypeIndex = (int)step.CheckType;
        if (ImGui.Combo(" Method Type", ref checkTypeIndex, Enum.GetNames(typeof(ConditionalCheckType)), GetEnumLength<ConditionalCheckType>()))
            step.CheckType = (ConditionalCheckType)checkTypeIndex;
    }

    private static void DrawCurrencyAndAutoSuccessSection(CraftingStepInput step)
    {
        var currencyItem = step.CurrencyItem;
        var availableWidth = ImGui.GetContentRegionAvail().X * 0.75f;
        ImGui.SetNextItemWidth(availableWidth);
        if (ImGui.InputTextWithHint("Currency Item", "Case Sensitive Currency BaseName \"Orb of Transmutation\"...", ref currencyItem, 100_000))
            step.CurrencyItem = currencyItem;

        var autoSuccess = step.AutomaticSuccess;
        if (ImGui.Checkbox("Automatic Success", ref autoSuccess))
            step.AutomaticSuccess = autoSuccess;
    }

    private static void DrawSuccessActionSection(CraftingStepInput step, List<CraftingStepInput> steps, int stepIndex)
    {
        var successActionIndex = (int)step.SuccessAction;
        if (ImGui.Combo(" On Success##SuccessAction", ref successActionIndex, Enum.GetNames(typeof(SuccessAction)), Enum.GetNames(typeof(SuccessAction)).Length))
        {
            step.SuccessAction = (SuccessAction)successActionIndex;
        }

        if (step.SuccessAction == SuccessAction.GoToStep)
        {
            ImGui.SameLine();
            var dropdownIndex = step.SuccessActionStepIndex;
            var stepNames = new List<string>();
            for (var s = 0; s < steps.Count; s++)
            {
                if (s != stepIndex || stepIndex == dropdownIndex)
                {
                    var description = $"Step {s + 1} - ";
                    description += GetStepText(steps[s]);
                    if (stepIndex == dropdownIndex)
                    {
                        description = $"(LOOP) {description}";
                    }

                    stepNames.Add(description);
                }
            }

            dropdownIndex = dropdownIndex > stepIndex && dropdownIndex < steps.Count ? dropdownIndex - 1 : dropdownIndex;
            var comboItems = string.Join('\0', stepNames) + '\0';

            if (ImGui.Combo("##SuccessStepIndex", ref dropdownIndex, comboItems, stepNames.Count))
            {
                var selectedStepIndex = dropdownIndex >= stepIndex ? dropdownIndex + 1 : dropdownIndex;
                step.SuccessActionStepIndex = selectedStepIndex;
            }

            ImGui.SameLine();
            ImGui.Text("On Success");
        }
    }

    private static void DrawFailureActionSection(CraftingStepInput step, List<CraftingStepInput> steps, int stepIndex)
    {
        var failureActionIndex = (int)step.FailureAction;
        if (ImGui.Combo(" On Failure##FailureAction", ref failureActionIndex, Enum.GetNames(typeof(FailureAction)), Enum.GetNames(typeof(FailureAction)).Length))
        {
            step.FailureAction = (FailureAction)failureActionIndex;
        }

        if (step.FailureAction == FailureAction.GoToStep)
        {
            ImGui.SameLine();
            var dropdownIndex = step.FailureActionStepIndex;
            var stepNames = new List<string>();
            for (var s = 0; s < steps.Count; s++)
            {
                if (s != stepIndex || stepIndex == dropdownIndex)
                {
                    var description = $"Step {s + 1} - ";
                    description += GetStepText(steps[s]);
                    if (stepIndex == dropdownIndex)
                    {
                        description = $"(LOOP) {description}";
                    }

                    stepNames.Add(description);
                }
            }

            dropdownIndex = dropdownIndex > stepIndex && dropdownIndex < steps.Count ? dropdownIndex - 1 : dropdownIndex;
            var comboItems = string.Join('\0', stepNames) + '\0';

            if (ImGui.Combo("##FailureStepIndex", ref dropdownIndex, comboItems, stepNames.Count))
            {
                var selectedStep = dropdownIndex >= stepIndex ? dropdownIndex + 1 : dropdownIndex;
                step.FailureActionStepIndex = selectedStep;
            }

            ImGui.SameLine();
            ImGui.Text("On Failure");
        }
    }

    private static void DrawBranchEditor(CraftingStepInput step, CraftingStepBranchInput branch, List<CraftingStepInput> steps, int stepIndex, int branchIndex)
    {
        var branchName = step.Branches[branchIndex].ConditionalGroups[0].Conditionals[0].Name;
        var branchFilterValue = step.Branches[branchIndex].ConditionalGroups[0].Conditionals[0].Value;
        var (isCompiled, errorMessage) = CheckFilterCompilation(branchFilterValue);

        if (!isCompiled)
        {
            using (new ErrorStyling())
            {
                if (ImGui.InputTextWithHint("##conditionName", "Branch Name", ref branchName, 100_000))
                    step.Branches[branchIndex].ConditionalGroups[0].Conditionals[0].Name = branchName;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(ErrorStyling.PastelRed, errorMessage);
                ImGui.EndTooltip();
            }
        }
        else
        {
            if (ImGui.InputTextWithHint("##conditionName", "Branch Name", ref branchName, 100_000))
                step.Branches[branchIndex].ConditionalGroups[0].Conditionals[0].Name = branchName;
        }

        ImGui.SameLine();
        ImGui.PushID("editbutton");
        DrawConditionalEditButton(step, stepIndex, branchIndex, 0, 0);
        ImGui.PopID();

        ImGui.SameLine();

        var failureActionIndex = (int)branch.MatchAction;
        var actionNames = Enum.GetNames<AnyAction>();
        var maxItemWidth = actionNames.Max(name => ImGui.CalcTextSize(name).X);
        ImGui.SetNextItemWidth(maxItemWidth + 60);

        if (ImGui.Combo("##MatchAction", ref failureActionIndex, actionNames, actionNames.Length))
        {
            branch.MatchAction = (AnyAction)failureActionIndex;
        }

        if (branch.MatchAction == AnyAction.GoToStep)
        {
            ImGui.SameLine();
            var dropdownIndex = branch.MatchActionStepIndex;
            var stepNames = new List<string>();
            for (var s = 0; s < steps.Count; s++)
            {
                if (s != stepIndex || stepIndex == dropdownIndex)
                {
                    var description = $"Step {s + 1} - ";
                    description += GetStepText(steps[s]);
                    if (stepIndex == dropdownIndex)
                    {
                        description = $"(LOOP) {description}";
                    }

                    stepNames.Add(description);
                }
            }

            dropdownIndex = dropdownIndex > stepIndex && dropdownIndex < steps.Count ? dropdownIndex - 1 : dropdownIndex;
            var comboItems = string.Join('\0', stepNames) + '\0';

            if (ImGui.Combo("##MatchStepIndex", ref dropdownIndex, comboItems, stepNames.Count))
            {
                var selectedStep = dropdownIndex >= stepIndex ? dropdownIndex + 1 : dropdownIndex;
                branch.MatchActionStepIndex = selectedStep;
            }

            ImGui.SameLine();
            ImGui.Text("On Match");
        }
    }

    private static void DrawCopyConditionalGroupsSection(CraftingStepInput step, List<CraftingStepInput> steps, int stepIndex)
    {
        var stepsWithIndex = steps.Select(
            (s, idx) => new
            {
                Step = s,
                Index = idx
            });

        var stepNamesForDropdown = stepsWithIndex.Where(x => x.Index != stepIndex).Select(x => $"STEP [{x.Index + 1}]").ToArray();

        if (stepNamesForDropdown.Length > 0)
        {
            var currentStepIndex = -1;
            var maxItemWidth = stepNamesForDropdown.Max(name => ImGui.CalcTextSize(name).X);
            ImGui.SetNextItemWidth(maxItemWidth + 60);

            if (ImGui.Combo("Copy Conditional Groups From", ref currentStepIndex, stepNamesForDropdown, stepNamesForDropdown.Length))
            {
                ResetEditingIdentifiers();

                if (currentStepIndex >= stepIndex)
                    currentStepIndex++;

                if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
                {
                    var sourceStep = steps[currentStepIndex];
                    step.ConditionalGroups = sourceStep
                        .ConditionalGroups
                        .Select(
                            group => new ConditionalChecksGroupInput
                            {
                                GroupType = group.GroupType,
                                ConditionalsToBePassForSuccess = group.ConditionalsToBePassForSuccess,
                                Conditionals = group
                                    .Conditionals
                                    .Select(
                                        conditional => new ConditionalKeys
                                        {
                                            Name = conditional.Name,
                                            Value = conditional.Value
                                        })
                                    .ToList()
                            })
                        .ToList();
                }
            }
        }
    }

    private static void DrawAddConditionalGroupButton(CraftingStepInput step)
    {
        using (AdditionButton)
        {
            if (ImGui.Button("Add Conditional Group"))
            {
                ResetEditingIdentifiers();
                step.ConditionalGroups.Add(new ConditionalChecksGroupInput());
            }
        }
    }

    private static void DrawConditionalGroups(CraftingStepInput step, List<CraftingStepInput> steps, int stepIndex)
    {
        var groupsToRemove = new List<int>();

        for (var groupIndex = 0; groupIndex < step.ConditionalGroups.Count; groupIndex++)
        {
            ImGui.PushID(groupIndex);
            Color bgColor = step.ConditionalGroups[groupIndex].GroupType switch
            {
                ConditionGroup.AND => Main.Settings.Styling.ConditionGroupBackgrounds.And,
                ConditionGroup.OR => Main.Settings.Styling.ConditionGroupBackgrounds.Or,
                ConditionGroup.NOT => Main.Settings.Styling.ConditionGroupBackgrounds.Not,
                _ => Main.Settings.Styling.ConditionGroupBackgrounds.And
            };

            using (new ColorBackground((ImGuiCol.ChildBg, bgColor)))
            {
                ImGui.BeginChild("ConditionalGroup", Vector2.Zero, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);

                var removeGroup = DrawConditionalGroupHeader(step, groupIndex);
                if (removeGroup)
                    groupsToRemove.Add(groupIndex);
                else
                    DrawConditionalChecks(step, stepIndex, groupIndex);

                ImGui.EndChild();
            }

            ImGui.PopID();
        }

        foreach (var idx in groupsToRemove.OrderByDescending(i => i))
        {
            step.ConditionalGroups.RemoveAt(idx);
        }
    }

    private static bool DrawConditionalGroupHeader(CraftingStepInput step, int groupIndex)
    {
        var removeRequested = false;

        using (RemovalButton)
        {
            if (ImGui.Button("Remove Group"))
                removeRequested = true;
        }

        ImGui.SameLine();
        if (groupIndex > 0)
        {
            if (ImGui.ArrowButton("Up", ImGuiDir.Up))
            {
                (step.ConditionalGroups[groupIndex - 1], step.ConditionalGroups[groupIndex])
                    = (step.ConditionalGroups[groupIndex], step.ConditionalGroups[groupIndex - 1]);
            }
        }

        ImGui.SameLine();
        if (groupIndex < step.ConditionalGroups.Count - 1)
        {
            if (ImGui.ArrowButton("Down", ImGuiDir.Down))
            {
                (step.ConditionalGroups[groupIndex + 1], step.ConditionalGroups[groupIndex])
                    = (step.ConditionalGroups[groupIndex], step.ConditionalGroups[groupIndex + 1]);
            }
        }

        ImGui.SameLine();
        var groupTypeIndex = (int)step.ConditionalGroups[groupIndex].GroupType;
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo(" Group Type", ref groupTypeIndex, Enum.GetNames(typeof(ConditionGroup)), Enum.GetNames(typeof(ConditionGroup)).Length))
            step.ConditionalGroups[groupIndex].GroupType = (ConditionGroup)groupTypeIndex;

        using (AdditionButton)
        {
            if (ImGui.Button("Add Conditional Check"))
            {
                ResetEditingIdentifiers();
                step.ConditionalGroups[groupIndex].Conditionals.Add(new ConditionalKeys());
            }
        }

        if (step.ConditionalGroups[groupIndex].GroupType != ConditionGroup.NOT)
        {
            ImGui.SameLine();
            var reqChecks = step.ConditionalGroups[groupIndex].ConditionalsToBePassForSuccess;
            if (ImGui.InputInt("Req Checks to Pass", ref reqChecks))
            {
                reqChecks = Math.Max(1, Math.Min(reqChecks, step.ConditionalGroups[groupIndex].Conditionals.Count));
                step.ConditionalGroups[groupIndex].ConditionalsToBePassForSuccess = reqChecks;
            }
        }

        return removeRequested;
    }

    private static void DrawConditionalChecks(CraftingStepInput step, int stepIndex, int groupIndex)
    {
        ImGui.Indent();
        var checksToRemove = new List<int>();

        for (var conditionalIndex = 0; conditionalIndex < step.ConditionalGroups[groupIndex].Conditionals.Count; conditionalIndex++)
        {
            ImGui.PushID(conditionalIndex);

            using (RemovalButton)
            {
                if (ImGui.Button("Remove"))
                {
                    ResetEditingIdentifiers();
                    checksToRemove.Add(conditionalIndex);
                    ImGui.PopID();
                    continue;
                }
            }

            ImGui.SameLine();
            var checkKey = step.ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Name;
            var filterValue = step.ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Value;
            var (isCompiled, errorMessage) = CheckFilterCompilation(filterValue);

            if (!isCompiled)
            {
                using (new ErrorStyling())
                {
                    var availableWidth = ImGui.GetContentRegionAvail().X * 0.75f;
                    ImGui.SetNextItemWidth(availableWidth);
                    if (ImGui.InputTextWithHint("##conditionName", "Name of condition...", ref checkKey, 100_000))
                        step.ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Name = checkKey;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextColored(ErrorStyling.PastelRed, errorMessage);
                    ImGui.EndTooltip();
                }
            }
            else
            {
                var availableWidth = ImGui.GetContentRegionAvail().X * 0.75f;
                ImGui.SetNextItemWidth(availableWidth);
                if (ImGui.InputTextWithHint("##conditionName", "Name of condition...", ref checkKey, 100_000))
                    step.ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Name = checkKey;
            }

            ImGui.SameLine();

            var branchIndex = -1;
            DrawConditionalEditButton(step, stepIndex, branchIndex, groupIndex, conditionalIndex);

            ImGui.PopID();
        }

        ImGui.Unindent();

        foreach (var idx in checksToRemove.OrderByDescending(i => i))
        {
            if (step.ConditionalGroups[groupIndex].Conditionals.Count >= step.ConditionalGroups[groupIndex].ConditionalsToBePassForSuccess &&
                step.ConditionalGroups[groupIndex].ConditionalsToBePassForSuccess > 1)
                step.ConditionalGroups[groupIndex].ConditionalsToBePassForSuccess--;

            step.ConditionalGroups[groupIndex].Conditionals.RemoveAt(idx);
        }
    }

    private static void DrawConditionalEditButton(CraftingStepInput step, int stepIndex, int branchIndex, int groupIndex, int conditionalIndex)
    {
        var isEditing = IsCurrentEditorContext(stepIndex, branchIndex, groupIndex, conditionalIndex);
        var editString = isEditing ? "Editing" : "Edit";

        using (isEditing ? AdditionButton : null)
        {
            if (ImGui.Button(editString))
            {
                if (isEditing)
                {
                    ResetEditingIdentifiers();
                }
                else
                {
                    condEditValue = branchIndex == -1
                        ? step.ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Value
                        : step.Branches[branchIndex].ConditionalGroups[groupIndex].Conditionals[conditionalIndex].Value;

                    tempCondValue = condEditValue;
                    Editor = new EditorRecord(groupIndex, branchIndex, stepIndex, conditionalIndex);
                }
            }
        }

        if (isEditing)
            ConditionValueEditWindow(step, stepIndex, branchIndex, groupIndex, conditionalIndex);
    }

    private static bool DrawStepManipulationControls(List<CraftingStepInput> steps, ref int stepIndex)
    {
        using (RemovalButton)
        {
            if (ImGui.Button("[-] Remove This Step"))
            {
                ResetEditingIdentifiers();
                steps.RemoveAt(stepIndex);
                if (stepIndex >= steps.Count)
                    stepIndex = Math.Max(0, steps.Count - 1);

                return true;
            }
        }

        return false;
    }

    private static void ConditionValueEditWindow(CraftingStepInput stepInput, int stepIndex, int branchIndex, int groupIndex, int conditionalIndex)
    {
        if (Editor.StepIndex != stepIndex ||
            Editor.GroupIndex != groupIndex ||
            Editor.ConditionalIndex != conditionalIndex ||
            Editor.BranchIndex != branchIndex)
            return;

        if (!ImGui.Begin("Edit Conditional", ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        var conditionalName = Editor.BranchIndex == -1
            ? Main
                .Settings
                .NonUserData
                .SelectedCraftingStepInputs[Editor.StepIndex]
                .ConditionalGroups[Editor.GroupIndex]
                .Conditionals[Editor.ConditionalIndex].Name
            : Main
                .Settings
                .NonUserData
                .SelectedCraftingStepInputs[Editor.StepIndex]
                .Branches[Editor.BranchIndex]
                .ConditionalGroups[Editor.GroupIndex]
                .Conditionals[Editor.ConditionalIndex].Name;

        ImGui.BulletText(
            Editor.BranchIndex == -1
                ? $"Editing: STEP[{Editor.StepIndex + 1}] => Group[{Editor.GroupIndex + 1}] => Conditional[{(!string.IsNullOrEmpty(conditionalName) ? conditionalName : Editor.ConditionalIndex + 1)}]"
                : $"Editing: STEP[{Editor.StepIndex + 1}] => Branch[{Editor.BranchIndex}] => Group[{Editor.GroupIndex + 1}] => Conditional[{(!string.IsNullOrEmpty(conditionalName) ? conditionalName : Editor.ConditionalIndex + 1)}]");

        if (ImGui.Button("Save"))
        {
            var oldValue = Editor.BranchIndex == -1
                ? stepInput.ConditionalGroups[Editor.GroupIndex].Conditionals[Editor.ConditionalIndex].Value
                : stepInput.Branches[Editor.BranchIndex].ConditionalGroups[Editor.GroupIndex].Conditionals[Editor.ConditionalIndex].Value;

            InvalidateFilterCache(oldValue);

            if (Editor.BranchIndex == -1)
            {
                stepInput.ConditionalGroups[Editor.GroupIndex].Conditionals[Editor.ConditionalIndex].Value = tempCondValue;
            }
            else
            {
                stepInput.Branches[Editor.BranchIndex].ConditionalGroups[Editor.GroupIndex].Conditionals[Editor.ConditionalIndex].Value = tempCondValue;
            }

            InvalidateFilterCache(tempCondValue);
            ResetEditingIdentifiers();
        }

        ImGui.SameLine();

        if (ImGui.Button("Revert"))
            tempCondValue = condEditValue;

        ImGui.SameLine();

        if (ImGui.Button("Close"))
            ResetEditingIdentifiers();

        ImGui.SameLine();
        DrawCopyConditionalCombo();

        ImGui.InputTextMultiline("##text_edit", ref tempCondValue, 15000, ImGui.GetContentRegionAvail(), ImGuiInputTextFlags.AllowTabInput);

        ImGui.End();
    }

    private static void DrawCopyConditionalCombo()
    {
        var allConditionalsWithStepInfo = Main
            .Settings
            .NonUserData
            .SelectedCraftingStepInputs
            .SelectMany(
                (s, sIdx) => s.ConditionalGroups.SelectMany(
                    grp => grp.Conditionals, (grp, cond) => new
                    {
                        sIdx,
                        cond
                    }))
            .ToList();

        var conditionalNames = allConditionalsWithStepInfo
            .Select((c, i) => "Step " + (c.sIdx + 1) + ": " + (string.IsNullOrEmpty(c.cond.Name) ? $"Unnamed Conditional {i + 1}" : c.cond.Name))
            .ToArray();

        var selectedIndex = -1;
        ImGui.SetNextItemWidth(300);

        if (ImGui.Combo("Copy Conditional From", ref selectedIndex, conditionalNames, conditionalNames.Length))
            if (selectedIndex >= 0 && selectedIndex < allConditionalsWithStepInfo.Count)
                tempCondValue = allConditionalsWithStepInfo[selectedIndex].cond.Value;
    }

    private static bool IsCurrentEditorContext(int stepIndex, int branchIndex, int groupIndex, int conditionalIndex) =>
        Editor.StepIndex == stepIndex && Editor.GroupIndex == groupIndex && Editor.ConditionalIndex == conditionalIndex && Editor.BranchIndex == branchIndex;

    private static (bool isCompiled, string errorMessage) CheckFilterCompilation(string filterValue)
    {
        if (string.IsNullOrWhiteSpace(filterValue))
            return (true, "");

        if (_filterCompilationCache.TryGetValue(filterValue, out var cachedResult))
            return cachedResult;

        var filter = ItemFilter.LoadFromString(filterValue);
        var isCompiled = !(filter.Queries.Count == 0 || filter.Queries.All(q => q.Query.FailedToCompile));
        var errorMessage = "";

        if (!isCompiled)
        {
            var failedQuery = filter.Queries.FirstOrDefault(q => q.Query.FailedToCompile);
            errorMessage = failedQuery.Query != null
                ? $"[ItemQueryProcessor] Error processing query ({failedQuery.Query.RawQuery}) on Line # {failedQuery.Query.InitialLine}: {failedQuery.Query.Error}"
                : "Unknown compilation error";
        }

        var result = (isCompiled, errorMessage);
        _filterCompilationCache[filterValue] = result;
        return result;
    }

    private static void InvalidateFilterCache(string filterValue)
    {
        _filterCompilationCache.Remove(filterValue);
    }

    private static void ResetEditingIdentifiers()
    {
        Editor = new EditorRecord(-1, -1, -1, -1);
    }

    private static void DrawConfirmAndClear()
    {
        if (!ImGui.CollapsingHeader("Confirm / Clear Steps", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        var stepCount = Main.SelectedCraftingSteps.FirstOrDefault()?.CraftingSteps.Count;
        ImGui.TextWrapped($"Steps applied: {Main.SelectedCraftingSteps.Count} items, {stepCount ?? 0} steps for the first one");
        if (ImGui.Button("[+] Apply Steps"))
        {
            Main.SelectedCraftingSteps.Clear();

            if (Main.Settings.RunOptions.CraftInventoryInsteadOfCurrencyTab)
                ApplyStepsForInventory();
            else
                ApplyStepsForCurrencyTab();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("This includes inventory setup\nif an item was removed since last apply\nAPPLY AGAIN");
            ImGui.EndTooltip();
        }

        ImGui.SameLine();

        if (ImGui.Button("[x] Clear All"))
            ImGui.OpenPopup(DeletePopup);

        if (ShowButtonPopup(DeletePopup, ["Are you sure?", "STOP"], out var clearSelectedIndex))
            if (clearSelectedIndex == 0)
            {
                Main.Settings.NonUserData.SelectedCraftingStepInputs.Clear();
                Main.SelectedCraftingSteps.Clear();
                Main.Settings.NonUserData.SelectedCraftingStepInputs.Insert(0, new CraftingStepInput());
            }

        ImGui.Unindent();
    }

    public static void ApplyStepsForInventory()
    {
        var itemsInInventory = InventoryHandler.TryGetValidCraftingItemsFromAnInventory(InventorySlotE.MainInventory1).ToList();
        var filter = GetInventoryFilterQuery();

        for (var col = 0; col < 12; col++)
        for (var row = 0; row < 5; row++)
        {
            var isValidAndSelected = itemsInInventory.Any(item => item.PosX == col && item.PosY == row) &&
                                     Main.Settings.RunOptions.InventoryCraftingSlots[row, col] == 1 &&
                                     (filter == null ||
                                      InventoryHandler.TryGetInventoryItemFromSlot(new Vector2(col, row), out var item) &&
                                      FilterHandler.IsItemMatchingCondition(item.Item, new ItemFilter([filter])));

            if (!isValidAndSelected)
                continue;

            var newCraftingBase = new CraftingBase
            {
                CraftingSteps = [],
                CraftingPosition = new Vector2(col, row)
            };

            newCraftingBase.MethodReadInventoryItem
                = async token => await InventoryHandler.AsyncTryGetInventoryItemFromSlot(newCraftingBase.CraftingPosition, token);

            for (var stepIndex = 0; stepIndex < Main.Settings.NonUserData.SelectedCraftingStepInputs.Count; stepIndex++)
            {
                var input = Main.Settings.NonUserData.SelectedCraftingStepInputs[stepIndex];

                async SyncTask<(bool result, bool isMatch)> ItemCheckApplicator(ItemFilter filter, CancellationToken token)
                {
                    return await FilterHandler.AsyncIsMatchingCondition(filter, newCraftingBase.CraftingPosition, token);
                }

                async SyncTask<bool> OrbApplicator(CancellationToken token)
                {
                    return await ItemHandler.AsyncTryApplyOrbToSlot(newCraftingBase.CraftingPosition, input.CurrencyItem, token);
                }

                if (!TryBuildStep(input, OrbApplicator, ItemCheckApplicator, stepIndex + 1, out var newStep))
                {
                    Logging.Logging.LogMessage(
                        "CraftingSequenceMenu: Failed to build step due to ItemFilter compiler error, clearing entire crafting sequence and stopping any running operations.",
                        LogMessageType.Error);

                    Main.SelectedCraftingSteps.Clear();
                    Main.Stop();
                    return;
                }

                newCraftingBase.CraftingSteps.Add(newStep);
            }

            Main.SelectedCraftingSteps.Add(newCraftingBase);
        }

        Logging.Logging.LogMessage(
            $"CraftingSequenceMenu: {Main.SelectedCraftingSteps.Count} items added with a step count of {Main.SelectedCraftingSteps.FirstOrDefault()?.CraftingSteps.Count}",
            LogMessageType.Info);
    }

    public static ItemQuery GetInventoryFilterQuery()
    {
        ItemQuery filter = null;
        if (Main.Settings.RunOptions.ApplyFilter && !string.IsNullOrWhiteSpace(Main.Settings.RunOptions.InventoryFilter.Value))
        {
            filter = ItemQuery.Load(Main.Settings.RunOptions.InventoryFilter.Value);
            if (filter.Error != null)
            {
                throw new Exception($"Inventory filter load error: {filter.Error}");
            }
        }

        return filter;
    }

    public static void ApplyStepsForCurrencyTab()
    {
        var newCraftingBase = new CraftingBase
        {
            CraftingSteps = [],
            MethodReadStashItem = async token => await StashHandler.AsyncTryGetStashSpecialSlot(SpecialSlot.CurrencyTab, token)
        };

        static async SyncTask<(bool result, bool isMatch)> ItemCheckApplicator(ItemFilter filter, CancellationToken token)
        {
            return await FilterHandler.AsyncIsMatchingCondition(filter, SpecialSlot.CurrencyTab, token);
        }

        for (var stepIndex = 0; stepIndex < Main.Settings.NonUserData.SelectedCraftingStepInputs.Count; stepIndex++)
        {
            var input = Main.Settings.NonUserData.SelectedCraftingStepInputs[stepIndex];

            async SyncTask<bool> OrbApplicator(CancellationToken token)
            {
                return await ItemHandler.AsyncTryApplyOrbToSpecialSlot(SpecialSlot.CurrencyTab, input.CurrencyItem, token);
            }

            if (!TryBuildStep(input, OrbApplicator, ItemCheckApplicator, stepIndex + 1, out var newStep))
            {
                Logging.Logging.LogMessage(
                    "CraftingSequenceMenu: Failed to build step due to ItemFilter compiler error, clearing entire crafting sequence and stopping any running operations.",
                    LogMessageType.Error);

                Main.SelectedCraftingSteps.Clear();
                Main.Stop();
                return;
            }

            newCraftingBase.CraftingSteps.Add(newStep);
        }

        Main.SelectedCraftingSteps.Add(newCraftingBase);
        Logging.Logging.LogMessage(
            $"CraftingSequenceMenu: Currency Tab Item Added with a step count of {newCraftingBase.CraftingSteps.Count}", LogMessageType.Info);
    }

    private static bool TryBuildStep(CraftingStepInput input, Func<CancellationToken, SyncTask<bool>> method,
        Func<ItemFilter, CancellationToken, SyncTask<(bool result, bool isMatch)>> itemCheckApplicator, int stepNumber, out CraftingStep newStep)
    {
        newStep = new CraftingStep
        {
            Method = method,
            CheckType = input.CheckType,
            AutomaticSuccess = input.AutomaticSuccess,
            SuccessAction = input.SuccessAction.ToAnyAction(),
            SuccessActionStepIndex = input.SuccessActionStepIndex,
            FailureAction = input.FailureAction.ToAnyAction(),
            FailureActionStepIndex = input.FailureActionStepIndex,
            ConditionalCheckGroups = [],
            Branches = []
        };

        if (!input.AutomaticSuccess)
        {
            for (var groupIndex = 0; groupIndex < input.ConditionalGroups.Count; groupIndex++)
            {
                var conditionGroup = input.ConditionalGroups[groupIndex];
                if (!TryBuildConditionGroup(conditionGroup, itemCheckApplicator, stepNumber, groupIndex + 1, out var newGroup))
                    return false;

                newStep.ConditionalCheckGroups.Add(newGroup);
            }
        }

        for (var branchIndex = 0; branchIndex < input.Branches.Count; branchIndex++)
        {
            var branch = input.Branches[branchIndex];
            var builtBranch = new CraftingStepBranch
            {
                MatchAction = branch.MatchAction,
                MatchActionStepIndex = branch.MatchActionStepIndex,
                ConditionalGroups = []
            };

            for (var groupIndex = 0; groupIndex < branch.ConditionalGroups.Count; groupIndex++)
            {
                var conditionGroup = branch.ConditionalGroups[groupIndex];
                if (!TryBuildConditionGroup(conditionGroup, itemCheckApplicator, stepNumber, groupIndex + 1, $"Branch {branchIndex + 1}", out var newGroup))
                    return false;

                builtBranch.ConditionalGroups.Add(newGroup);
            }

            newStep.Branches.Add(builtBranch);
        }

        return true;
    }

    private static bool TryBuildConditionGroup(ConditionalChecksGroupInput conditionGroup,
        Func<ItemFilter, CancellationToken, SyncTask<(bool result, bool isMatch)>> itemCheckApplicator, int stepNumber, int groupNumber,
        out ConditionalChecksGroup newGroup) => TryBuildConditionGroup(conditionGroup, itemCheckApplicator, stepNumber, groupNumber, null, out newGroup);

    private static bool TryBuildConditionGroup(ConditionalChecksGroupInput conditionGroup,
        Func<ItemFilter, CancellationToken, SyncTask<(bool result, bool isMatch)>> itemCheckApplicator, int stepNumber, int groupNumber, string branchInfo,
        out ConditionalChecksGroup newGroup)
    {
        newGroup = new ConditionalChecksGroup
        {
            GroupType = conditionGroup.GroupType,
            ConditionalsToBePassForSuccess = conditionGroup.ConditionalsToBePassForSuccess,
            ConditionalChecks = []
        };

        for (var conditionalIndex = 0; conditionalIndex < conditionGroup.Conditionals.Count; conditionalIndex++)
        {
            var checkKey = conditionGroup.Conditionals[conditionalIndex];
            var filter = ItemFilter.LoadFromString(checkKey.Value);
            if (filter.Queries.Count == 0 || filter.Queries.All(q => q.Query.FailedToCompile))
            {
                var stepDescription = GetStepText(Main.Settings.NonUserData.SelectedCraftingStepInputs[stepNumber - 1]);
                var failedQuery = filter.Queries.FirstOrDefault(q => q.Query.FailedToCompile);
                var itemFilterError = failedQuery.Query != null
                    ? $"[ItemQueryProcessor] Error processing query ({failedQuery.Query.RawQuery}) on Line # {failedQuery.Query.InitialLine}: {failedQuery.Query.Error}"
                    : "Unknown compilation error";

                var locationDescription = string.IsNullOrEmpty(branchInfo)
                    ? $"Conditional Group {groupNumber} ({conditionGroup.GroupType})"
                    : $"{branchInfo} - Conditional Group {groupNumber} ({conditionGroup.GroupType})";

                Logging.Logging.LogMessage(
                    $"CRAFTING SEQUENCE ERROR:\n" +
                    $"  Step {stepNumber}: '{stepDescription}'\n" +
                    $"  {locationDescription}\n" +
                    $"  Conditional {conditionalIndex + 1}: '{checkKey.Name}'\n" +
                    $"  {itemFilterError}", LogMessageType.Error);

                Logging.Logging.LogMessage("Clearing entire crafting sequence to prevent errors.", LogMessageType.Error);
                return false;
            }

            newGroup.ConditionalChecks.Add(async token => await itemCheckApplicator(filter, token));
        }

        return true;
    }

    private static void DrawInstructions()
    {
        if (!ImGui.CollapsingHeader("Selected Step Instructions"))
            return;

        ImGui.Indent();
        var steps = Main.Settings.NonUserData.SelectedCraftingStepInputs.ToList();

        for (var index = 0; index < steps.Count; index++)
        {
            var currentStep = steps[index];
            var childWindowTitle = GetStepText(currentStep);

            CreateStepChildWindow(currentStep, index, childWindowTitle);

            if (currentStep.AutomaticSuccess)
                HandleAutomaticSuccess(currentStep);
            else
                HandleManualSuccess(currentStep);

            ImGui.Unindent();
            ImGui.EndChild();
            ImGui.NewLine();
        }

        ImGui.Unindent();
    }

    private static void CreateStepChildWindow(CraftingStepInput currentStep, int index, string title)
    {
        ImGui.PushID(index);
        ImGui.BeginChild($"[{index + 1}] {title}", Vector2.Zero, ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.Border);
        ImGui.Text($"[{index + 1}] {title}");
        ImGui.Indent();
        if (title.StartsWith("Use") && !currentStep.AutomaticSuccess)
            ImGui.Text("Check the item");

        ImGui.PopID();
    }

    private static void HandleAutomaticSuccess(CraftingStepInput currentStep)
    {
        switch (currentStep.SuccessAction)
        {
            case SuccessAction.GoToStep:
                ImGui.Text($"Then, go to step {currentStep.SuccessActionStepIndex + 1}");
                break;
            case SuccessAction.Continue:
                ImGui.Text("Then continue");
                break;
            case SuccessAction.End:
                ImGui.Text("Then End");
                break;
        }
    }

    private static void HandleManualSuccess(CraftingStepInput currentStep)
    {
        foreach (var group in currentStep.ConditionalGroups.OrderBy(g => g.GroupType))
        {
            ImGui.Text($"{group.GroupType} {group.ConditionalsToBePassForSuccess} or more of the following conditions pass:");
            ImGui.Indent();
            foreach (var conditional in group.Conditionals)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.Text(conditional.Name);
            }

            ImGui.Unindent();
        }

        if (currentStep.SuccessAction == SuccessAction.GoToStep)
            ImGui.Text($"If so, go to step {currentStep.SuccessActionStepIndex + 1}");
        else if (currentStep.SuccessAction == SuccessAction.End)
            ImGui.Text("If so, the item is done!");

        switch (currentStep.FailureAction)
        {
            case FailureAction.GoToStep:
                ImGui.Text($"If not, go to step {currentStep.FailureActionStepIndex + 1}");
                break;
            case FailureAction.Restart:
                ImGui.Text("If not, restart from first step");
                break;
            case FailureAction.RepeatStep:
                ImGui.Text("If not, repeat this step");
                break;
        }
    }

    private static readonly CachedValue<List<string>> LoadFilesCache = new TimeCache<List<string>>(CacheUtils.RememberLastValue<List<string>>((prev) =>
    {
        var newValue = GetFiles();
        if (prev?.ToHashSet().SetEquals(newValue) == true)
        {
            return prev;
        }

        return newValue;
    }), 1000);
    private static string _loadFilesFilter = "";

    private static void DrawFileOptions()
    {
        if (!ImGui.CollapsingHeader("Load / Save", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        ImGui.InputTextWithHint("##SaveAs", "File Path...", ref _fileSaveName, 100_000);
        ImGui.SameLine();

        if (ImGui.Button("Save To File"))
        {
            _files = GetFiles();
            _fileSaveName = HelperHandler.CleanWindowsString(_fileSaveName);

            if (_fileSaveName == string.Empty)
            {
                Logging.Logging.LogMessage("Attempted to save file without a name.", LogMessageType.Error);
            }
            else if (_files.Contains(_fileSaveName))
            {
                ImGui.OpenPopup(OverwritePopup);
                Logging.Logging.LogMessage($"File {_fileSaveName} already exists, requesting overwrite confirmation.", LogMessageType.Info);
            }
            else
            {
                SaveFile(Main.Settings.NonUserData.SelectedCraftingStepInputs, $"{_fileSaveName}.json");
                Logging.Logging.LogMessage($"File {_fileSaveName}.json saved successfully.", LogMessageType.Info);
            }
        }

        ImGui.Separator();

        if (ImGuiHelpers.SearchCombobox("Load file", ref _loadFilesFilter, ref _selectedFileName, LoadFilesCache.Value, ImGuiHelpers.WhitespaceSeparatedContains))
        {
            _fileSaveName = _selectedFileName;
            LoadFile(_selectedFileName);

            _filterCompilationCache.Clear();
            Logging.Logging.LogMessage($"File {_selectedFileName} loaded successfully.", LogMessageType.Info);
        }
        ImGui.SameLine();
        ImGui.Text("Load file");
        
        ImGui.Separator();

        if (ImGui.Button("Open Crafting Template Folder"))
        {
            var configDir = Main.ConfigDirectory;

            if (!Directory.Exists(configDir))
            {
                Logging.Logging.LogMessage("Unable to open config directory because it does not exist.", LogMessageType.Error);
            }
            else
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = configDir
                    });

                Logging.Logging.LogMessage("Opened config directory in explorer.", LogMessageType.Info);
            }
        }

        if (ShowButtonPopup(OverwritePopup, ["Are you sure?", "STOP"], out var saveSelectedIndex) && saveSelectedIndex == 0)
        {
            SaveFile(Main.Settings.NonUserData.SelectedCraftingStepInputs, $"{_fileSaveName}.json");
            Logging.Logging.LogMessage($"File {_fileSaveName}.json saved successfully after overwrite confirmation.", LogMessageType.Info);
        }

        ImGui.Unindent();
    }

    public static bool ShowButtonPopup(string popupId, List<string> items, out int selectedIndex)
    {
        selectedIndex = -1;
        var isItemClicked = false;
        var showPopup = true;

        if (!ImGui.BeginPopupModal(popupId, ref showPopup, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
            return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (ImGui.Button(items[i]))
            {
                selectedIndex = i;
                isItemClicked = true;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
        }

        ImGui.EndPopup();
        return isItemClicked;
    }

    private static void DrawImportSettings()
    {
        if (!ImGui.CollapsingHeader("Import Craft of Exile Skeleton", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();

        if (ImGui.Button("Refresh CoE Data"))
        {
            FetchNewCoeLangData();
            LoadCoELangData();
            FetchNewCoeDataData();
            LoadCoEDataData();
        }

        ImGui.InputTextWithHint("##CoEImport", "Craft of Exile Export String..", ref _coeImportData, 10_000_000);
        ImGui.SameLine();
        if (ImGui.Button("Import CoE Data"))
        {
            if (_coELang == null)
            {
                LoadCoELangData();
            }
            if (_coeData == null)
            {
                LoadCoEDataData();
            }

            Main.Settings.NonUserData.SelectedCraftingStepInputs = ConvertCoEData(_coeImportData);
        }

        ImGui.Unindent();
    }

    public static List<CraftingStepInput> ConvertCoEData(string coeJson)
    {
        var output = new List<CraftingStepInput>();
        var coeSim = JsonConvert.DeserializeObject<CoESimulator>(coeJson);
        foreach (var config in coeSim.config)
        {
            var input = new CraftingStepInput();
            if (config.method is {Count: > 1} && config.method[0]?.Equals("currency", StringComparison.OrdinalIgnoreCase) == true)
            {
                input.CurrencyItem = CoECurrencyDict.OrbNames.GetValueOrDefault(config.method[1], config.method[1]);
            }
            else
            {
                // Fallback inference if CoE didn't attach explicit currency
                if (coeSim.settings?.rarity?.Equals("magic", StringComparison.OrdinalIgnoreCase) == true)
                    input.CurrencyItem = "Orb of Alteration";
                else if (coeSim.settings?.rarity?.Equals("rare", StringComparison.OrdinalIgnoreCase) == true)
                    input.CurrencyItem = "Chaos Orb";
                else if (coeSim.settings?.rarity?.Equals("normal", StringComparison.OrdinalIgnoreCase) == true)
                    input.CurrencyItem = "Orb of Transmutation";
                else
                    input.CurrencyItem = "";
            }

            input.AutomaticSuccess = config.autopass;
            if (config.actions != null)
            {
                input.SuccessAction = config.actions.win?.ToLowerInvariant() == "end"
                    ? SuccessAction.End
                    : config.actions.win?.ToLowerInvariant() == "step"
                        ? SuccessAction.GoToStep
                        : SuccessAction.Continue;

                input.SuccessActionStepIndex = config.actions.win_route.HasValue ? (int)config.actions.win_route.Value - 1 : 1;
                input.FailureAction = config.actions.fail?.ToLowerInvariant() == "step"
                    ? FailureAction.GoToStep
                    : config.actions.fail?.ToLowerInvariant() == "loop"
                        ? FailureAction.RepeatStep
                        : FailureAction.Restart;

                input.FailureActionStepIndex = config.actions.fail_route.HasValue ? (int)config.actions.fail_route.Value - 1 : 1;
            }

            input.ConditionalGroups = new List<ConditionalChecksGroupInput>();
            if (config.filters != null)
            {
                foreach (var filter in config.filters)
                {
                    var group = new ConditionalChecksGroupInput
                    {
                        GroupType = filter.type?.ToLowerInvariant() == "or"
                            ? ConditionGroup.OR
                            : filter.type?.ToLowerInvariant() == "not"
                                ? ConditionGroup.NOT
                                : ConditionGroup.AND,
                        ConditionalsToBePassForSuccess = filter.treshold.HasValue ? (int)filter.treshold.Value : 1,
                        Conditionals = []
                    };

                    foreach (var cond in filter.conds)
                    {
                        var rawModName = _coELang.mod.GetValueOrDefault(cond.id, cond.id);
                        var cleanModName = Regex.Replace(rawModName, @"[#%+]+", "").Trim();
                        var ck = new ConditionalKeys
                        {
                            Name = $"{rawModName} (min:{cond.treshold}{(cond.max != null ? $", max:{cond.max}" : "")})",
                            Value = CoEQueryConverter.GenerateLinqQuery(cond.id, cleanModName, cond.treshold, _coeData)
                        };

                        group.Conditionals.Add(ck);
                    }

                    input.ConditionalGroups.Add(group);
                }
            }

            if (config.method is {Count: > 0} && config.method[0]?.Equals("check", StringComparison.OrdinalIgnoreCase) == true)
                input.CheckType = ConditionalCheckType.ConditionalCheckOnly;
            else
                input.CheckType = ConditionalCheckType.ModifyThenCheck;

            output.Add(input);
        }

        return output;
    }

    private static void LoadCoELangData()
    {
        var coeLangJson = "coe_lang.json";
        var fullPath = Path.Combine(Main.DirectoryFullName, "ExternalData", coeLangJson);
        try
        {
            if (!File.Exists(fullPath))
            {
                FetchNewCoeLangData();
            }

            var fileContent = File.ReadAllText(fullPath);
            _coELang = JsonConvert.DeserializeObject<CoELang>(fileContent);
            DebugWindow.LogMsg($"[CoeLang] Loaded file from {coeLangJson} with {_coELang.mod.Count} mods");
        }
        catch (Exception e)
        {
            Logging.Logging.LogMessage($"[CoeLang] Error loading file from {coeLangJson}: {e.Message}", LogMessageType.Error);
        }
    }

    private static void LoadCoEDataData()
    {
        var coeDataJson = "coe_data.json";
        var fullPath = Path.Combine(Main.DirectoryFullName, "ExternalData", coeDataJson);
        try
        {
            if (!File.Exists(fullPath))
            {
                FetchNewCoeDataData();
            }

            var fileContent = File.ReadAllText(fullPath);
            _coeData = JObject.Parse(fileContent);
            DebugWindow.LogMsg($"[CoeData] Loaded file from {coeDataJson}");
        }
        catch (Exception e)
        {
            DebugWindow.LogError($"[CoeData] Error loading file from {coeDataJson}: {e.Message}");
        }
    }

    private static void FetchNewCoeDataData()
    {
        const string url = "https://www.craftofexile.com/json/data/main/poec_data.json";
        const string prefix = "poecd=";
        const string coeDataJson = "coe_data.json";
        var fullPath = Path.Combine(Main.DirectoryFullName, "ExternalData", coeDataJson);

        Directory.CreateDirectory(Path.Combine(Main.DirectoryFullName, "ExternalData"));

        try
        {
            using var client = new WebClient();
            var content = client.DownloadString(url);

            if (content.StartsWith(prefix))
            {
                content = content[prefix.Length..];
                if (content.EndsWith(";"))
                {
                    content = content[..^1];
                }
            }

            File.WriteAllText(fullPath, content);

            DebugWindow.LogMsg($"[CoeData] Fetched new JSON from remote URL and saved to {coeDataJson}");
        }
        catch (Exception e)
        {
            DebugWindow.LogError($"[CoeData] Error fetching remote JSON: {e.Message}");
        }
    }

    private static void FetchNewCoeLangData()
    {
        const string url = "https://www.craftofexile.com/json/data/lang/poec_lang.us.json";
        const string prefix = "poecl=";
        const string coeLangJson = "coe_lang.json";
        var fullPath = Path.Combine(Main.DirectoryFullName, "ExternalData", coeLangJson);

        Directory.CreateDirectory(Path.Combine(Main.DirectoryFullName, "ExternalData"));

        try
        {
            using var client = new WebClient();
            var content = client.DownloadString(url);

            if (content.StartsWith(prefix))
            {
                content = content[prefix.Length..];
                if (content.EndsWith(";"))
                {
                    content = content[..^1];
                }
            }

            File.WriteAllText(fullPath, content);

            Logging.Logging.LogMessage($"[CoeLang] Fetched new JSON from remote URL and saved to {coeLangJson}", LogMessageType.Info);
        }
        catch (Exception e)
        {
            Logging.Logging.LogMessage($"[CoeLang] Error fetching remote JSON: {e.Message}", LogMessageType.Error);
        }
    }

    private record EditorRecord(int GroupIndex, int BranchIndex, int StepIndex, int ConditionalIndex);
}