using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AdjustDisabledWork;
[HarmonyPatch]
public static class Patch_Precept {
    private static readonly ConditionalWeakTable<Precept_Role, State> table = new();
    public const float ColumnMargin   = 16f;
    public const float RowMargin      =  4f;
    public const float CheckboxMargin =  8f;
    public const float RowSpace       = Widgets.CheckboxSize + RowMargin;

    // From Dialog_EditPrecept
    public const float BottomSpace    = 10f + 38f; 
    public const float HeaderHeight   = 35f;
    public const float HeaderMargin   = 10f;
    public const float HeaderSpace    = HeaderHeight + HeaderMargin;
    public const float UsableWidth    = 700f - 2 * 18f;

    private static State For(Precept_Role role)
        => (role?.def == null) ? null : table.GetValue(role, x => new(x));


    private static (WorkTags tag, string label)[] workTags;
    private static float[] columnWidths;
    private static float[] columns;
    private static float openHeight = 0f;

    private static bool open;
    private static float height;



    // Save, load, copy

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.CopyTo))]
    public static void CopyTo(Precept_Role __instance, Precept precept) 
        => For(__instance).CopyTo(For(precept as Precept_Role));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.ExposeData))]
    public static void ExposeData(Precept_Role __instance) 
        => For(__instance).ExposeData();



    // Editing precept

    private static void Layout() {
        if (workTags != null) return;

        workTags = Enum.GetValues(typeof(WorkTags))
            .OfType<WorkTags>()
            .Select(x => (x, $"WorkTag{x}".Translate().ToString()))
            .ToArray();

        Text.Font = GameFont.Small;
        float add = Widgets.CheckboxSize + CheckboxMargin;
        float[] widths = workTags
            .Select(x => Text.CalcSize(x.label).x + add)
            .ToArray();

        int rows, cols = (int) (UsableWidth / widths.Min()) + 1;
        do {
            cols--;
            rows = (widths.Length + cols - 1) / cols;
            columnWidths = widths
                .Select((x, i) => (x, i))
                .GroupBy(y => y.i / rows, (i, z) => z.Max(y => y.x))
                .ToArray();
        } while (cols > 1 && columnWidths.Sum() + (columnWidths.Length - 1) * ColumnMargin > UsableWidth);
        if (cols == 1) {
            columnWidths[0] = Mathf.Min(columnWidths[0], UsableWidth);
            columns = [ 0f ];
        } else {
            columns = new float[columnWidths.Length];
            float prev = 0f;
            for (int i = 0; i < columnWidths.Length; i++) {
                columns[i] = prev;
                prev += columnWidths[i] + ColumnMargin;
            }
        }

        openHeight = HeaderSpace + rows * RowSpace;
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Dialog_EditPrecept), MethodType.Constructor, typeof(Precept))]
    public static void EditPrecept_Costructor(Precept precept) {
        if (precept is not Precept_Role role) return;
        var state = For(role);
        state.Reset();
        open = state.WantsRoles;
    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(Dialog_EditPrecept), nameof(Dialog_EditPrecept.DoWindowContents))]
    public static void EditPrecept_DoWindowContents(Rect rect, Dialog_EditPrecept __instance, Precept ___precept) {
        if (___precept is not Precept_Role role) return;
        var state = For(role);
        Layout();
        rect = new(rect.x, rect.yMax - height - BottomSpace, rect.width, openHeight);

        Rect header = rect.TopPartPixels(HeaderHeight);
        Text.Font = GameFont.Medium;
        Widgets.Label(header, Strings.Title);
        header.xMin += Text.CalcSize(Strings.Title).x + 6f;
        Text.Font = GameFont.Small;

        bool wasOpen = open;
        Widgets.Checkbox(new(header.x, header.y + 4f), ref open, texChecked: TexButton.Collapse, texUnchecked: TexButton.Reveal);

        if (open) {
            var button = header.RightPartPixels(Text.CalcSize(Strings.Default).x + 16f);
            button.yMin += button.height - Text.LineHeight - 10f;
            TooltipHandler.TipRegion(button, Strings.DefaultTip);
            if (Widgets.ButtonText(button, Strings.Default)) {
                state.Unset();
            }

            int items = workTags.Length, cols = columns.Length, rows = (items + cols - 1) / cols;
            rect.yMin += HeaderHeight + HeaderMargin;
            rect.xMin += (rect.width - columns[cols - 1] - columnWidths[cols - 1]) / 2;
            for (int i = 0; i < items; i++) {
                int row = i % rows, col = i / rows;
                Rect r = new(rect.x + columns[col], rect.y + row * RowSpace, columnWidths[col], Widgets.CheckboxSize);
                DoCheck(r, i, state);
            }
        }

        if (wasOpen != open) {
            Traverse.Create(__instance).Method("UpdateWindowHeight").GetValue();
        }
    }

    private static void DoCheck(Rect rect, int i, State state) {
        WorkTags tag = workTags[i].tag;
        bool pre = (state.edit & tag) != 0, post = pre;
        Widgets.CheckboxLabeled(rect, workTags[i].label, ref post);
        if (pre != post) {
            if (post) {
                state.edit |= tag;
            } else {
                state.edit ^= tag;
            }
        }
    }
    

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Dialog_EditPrecept), "UpdateWindowHeight")]
    public static IEnumerable<CodeInstruction> EditPrecept_UpdateWindowHeight_Trans(IEnumerable<CodeInstruction> original) {
        bool found = false;
        FieldInfo windowHeight = AccessTools.Field(typeof(Dialog_EditPrecept), "windowHeight");
        FieldInfo precept      = AccessTools.Field(typeof(Dialog_EditPrecept), "precept");

        foreach (CodeInstruction instruction in original) {
            if (!found && instruction.operand as FieldInfo == windowHeight) {
                found = true;

                yield return new(OpCodes.Ldarg_0);
                yield return new(OpCodes.Ldfld, precept);
                yield return CodeInstruction.Call(typeof(Patch_Precept), nameof(ExtraWindowHeight));
                yield return new(OpCodes.Add);
            }

            yield return instruction;
        }
    }

    public static float ExtraWindowHeight(Precept precept) {
        if (precept is not Precept_Role) return 0f;
        Layout();
        height = open ? openHeight : HeaderHeight;
        return height + HeaderMargin;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Dialog_EditPrecept), "ApplyChanges")]
    public static void EditPrecept_ApplyChanges(Precept ___precept) {
        if (___precept is not Precept_Role role) return;
        For(role)?.Apply();
    }



    // Reading disabled work tags field

    private static WorkTags replacedTagsValue;
    private static bool replacedTags = false;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.DisabledWorkTypes), MethodType.Getter)]
    public static void Precept_DisabledWorkTypes_Pre(Precept_Role __instance) 
        => ReadDisabledTags_Pre(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.DisabledWorkTypes), MethodType.Getter)]
    public static void Precept_DisabledWorkTypes_Post(Precept_Role __instance) 
        => ReadDisabledTags_Post(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.GetTip))]
    public static void Precept_GetTip_Pre(Precept_Role __instance) 
        => ReadDisabledTags_Pre(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.GetTip))]
    public static void Precept_GetTip_Post(Precept_Role __instance) 
        => ReadDisabledTags_Post(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharacterCardUtility), "GetWorkTypeDisableCauses")]
    public static void GetWorkTypeDisableCauses_Pre(Precept_Role __instance) 
        => ReadDisabledTags_Pre(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterCardUtility), "GetWorkTypeDisableCauses")]
    public static void GetWorkTypeDisableCauses_Post(Precept_Role __instance) 
        => ReadDisabledTags_Post(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CombinedDisabledWorkTags), MethodType.Getter)]
    public static void Pawn_CombinedDisabledWorkTags_Pre(Pawn __instance) 
        => ReadDisabledTags_Pre(__instance.Ideo?.GetRole(__instance));

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CombinedDisabledWorkTags), MethodType.Getter)]
    public static void Pawn_CombinedDisabledWorkTags_Post(Pawn __instance) 
        => ReadDisabledTags_Post(__instance.Ideo?.GetRole(__instance));

    private static void ReadDisabledTags_Pre(Precept_Role role) {
        var state = For(role);
        if (state?.set ?? false) {
            replacedTagsValue = role.def.roleDisabledWorkTags;
            role.def.roleDisabledWorkTags = state.value;
            replacedTags = true;
        }
    }

    private static void ReadDisabledTags_Post(Precept_Role role) {
        if (replacedTags) {
            replacedTags = false;
            role.def.roleDisabledWorkTags = replacedTagsValue;
            replacedTagsValue = WorkTags.None;
        }
    }
}
