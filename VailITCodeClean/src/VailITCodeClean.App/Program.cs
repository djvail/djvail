namespace VailIT.CodeClean.App;

internal static class Program
{
    /// <summary>Starts the VailIT Code Clean window.</summary>
    [STAThread]
    private static void Main()
    {
        // Applies the settings in the .csproj (high-DPI mode, visual styles, default font).
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "VailIT Code Clean - unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
