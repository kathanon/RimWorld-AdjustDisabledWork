using HarmonyLib;
using Verse;
using UnityEngine;
using RimWorld;

namespace AdjustDisabledWork;

[StaticConstructorOnStartup]
public class Main : Mod {
    public static Main Instance { get; private set; }

    static Main() {
        var harmony = new Harmony(Strings.ID);
        harmony.PatchAll();
    }

    public Main(ModContentPack content) : base(content) {
        Instance = this;
    }
}
