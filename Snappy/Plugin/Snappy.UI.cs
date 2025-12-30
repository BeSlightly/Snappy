namespace Snappy;

public sealed partial class Snappy
{
    private void OnCommand(string command, string args)
    {
        args = args.Trim().ToLowerInvariant();

        if (args == "config")
        {
            ConfigWindow.IsOpen = true;
            return;
        }

        ToggleMainUI();
    }

    private void DrawUI()
    {
        while (_mainThreadActions.TryDequeue(out var action)) action.Invoke();

        WindowSystem.Draw();
        FileDialogManager.Draw();
    }

    public void DrawConfigUI()
    {
        ConfigWindow.Toggle();
    }

    public void ToggleMainUI()
    {
        MainWindow.Toggle();
    }
}
