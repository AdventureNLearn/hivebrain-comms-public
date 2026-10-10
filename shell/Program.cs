namespace Shell;

static class Program
{
    internal static readonly string LogPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shell-crash.log"));

    internal static void Log(string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine;
        File.AppendAllText(LogPath, line);
    }

    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log("UI " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("DOMAIN " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TASK " + e.Exception);
            e.SetObserved();
        };
        Application.ApplicationExit += (_, _) => Log("exit");
        Log("start");
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
        Log("run returned");
    }
}
