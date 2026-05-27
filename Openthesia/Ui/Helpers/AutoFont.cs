using ImGuiNET;

namespace Openthesia.Ui.Helpers;

public readonly struct AutoFont : IDisposable
{
    public AutoFont(ImFontPtr font)
    {
        ImGui.PushFont(font);
    }

    public void Dispose()
    {
        ImGui.PopFont();
    }
}
