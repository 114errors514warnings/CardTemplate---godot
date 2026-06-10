using Godot;

internal static class SceneConsoleRouter
{
    public static void AppendInfo(string message)
    {
        AppendRaw("[信息] " + message, false);
    }

    public static void AppendError(string message, bool alsoPrintError = true)
    {
        AppendRaw("[错误] " + message, alsoPrintError);
    }

    public static void AppendRaw(string message, bool alsoPrintError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (alsoPrintError)
        {
            GD.PrintErr(message);
        }

        Node scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
        RichTextLabel console = FindConsole(scene);
        if (console != null)
        {
            if (!string.IsNullOrEmpty(console.Text))
            {
                console.Text += "\n";
            }

            console.Text += message;
            return;
        }

        if (!alsoPrintError)
        {
            GD.Print(message);
        }
    }

    private static RichTextLabel FindConsole(Node scene)
    {
        if (scene == null)
        {
            return null;
        }

        RichTextLabel console = scene.GetNodeOrNull<RichTextLabel>("ConsoleContainer/Console");
        if (console != null)
        {
            return console;
        }

        console = scene.GetNodeOrNull<RichTextLabel>("DebugBattle/ConsoleContainer/Console");
        if (console != null)
        {
            return console;
        }

        return FindConsoleRecursive(scene);
    }

    private static RichTextLabel FindConsoleRecursive(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is RichTextLabel label && label.Name == "Console" && child.GetParent()?.Name == "ConsoleContainer")
            {
                return label;
            }

            RichTextLabel nested = FindConsoleRecursive(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}