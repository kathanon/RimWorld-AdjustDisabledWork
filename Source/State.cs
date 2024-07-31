using RimWorld;
using Verse;

namespace AdjustDisabledWork;
public class State {
    public WorkTags value;
    public WorkTags edit;
    public bool set;
    public PreceptDef def;

    public bool WantsWorkTypes 
        => (value | edit | def.roleDisabledWorkTags) != WorkTags.None;

    public State(Precept_Role role) {
        def = role.def;
        value = edit = def.roleDisabledWorkTags;
        set = false;
    }

    public void Reset() {
        edit = value;
    }

    public void Unset() {
        edit = def.roleDisabledWorkTags;
    }

    public void Apply() {
        value = edit;
        set = value != def.roleDisabledWorkTags;
    }

    public void CopyTo(State other) {
        if (other != null) {
            other.value = value;
            other.edit  = edit;
            other.set   = set;
            other.def   = def;
        }
    }

    public void ExposeData() {
        int save = set ? (int) value : -1;
        Scribe_Values.Look(ref save, Strings.ID, -1);
        if (Scribe.mode == LoadSaveMode.LoadingVars) {
            set = save > -1;
            if (set) {
                value = edit = (WorkTags) save;
            }
        }
    }
}
