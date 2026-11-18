using Grasshopper.Kernel;

namespace ConduitPlus.Grasshopper;

/// <summary>Standard GH preview controls for displays with no geometry outputs.</summary>
public abstract class PreviewDisplayComponent(string name, string nickname, string description)
    : GH_Component(name, nickname, description, "Conduit+", "Display"), IGH_PreviewObject
{
    public override bool IsPreviewCapable => true;

    // GH 8.20's non-overridable Hidden setter stores the flag but emits no event. In particular,
    // GH_HiddenAction (native undo/redo) sets IGH_PreviewObject.Hidden without ObjectChanged.
    // Reimplement that interface property while retaining GH's storage, archive and shading.
    public new bool Hidden
    {
        get => base.Hidden;
        set
        {
            if (base.Hidden == value) return;
            base.Hidden = value;
            OnObjectChanged(GH_ObjectEventType.Preview);
        }
    }
}
