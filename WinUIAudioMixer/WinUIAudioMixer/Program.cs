namespace WinUIAudioMixer;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Enforce single instance — if already running, wake the existing
        // window instead of silently exiting (looks like a failed launch).
        using var mutex = new System.Threading.Mutex(true, @"Global\BaumDash-SingleInstance", out bool isNewInstance);
        using var showSignal = new System.Threading.EventWaitHandle(
            false, System.Threading.EventResetMode.AutoReset, @"Local\BaumDash-ShowWindow");
        if (!isNewInstance)
        {
            showSignal.Set();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Services.CrashLogger.SessionStart();

        // Catch UI-thread exceptions and restart the process
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Services.CrashLogger.Fatal("Unhandled UI-thread exception — restarting", e.Exception);
            RestartAfterCrash();
        };

        // Catch non-UI-thread exceptions that would otherwise terminate the process
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Services.CrashLogger.Fatal(
                $"Unhandled non-UI exception (terminating={e.IsTerminating})", ex);
            if (e.IsTerminating) RestartAfterCrash();
        };

        var form = new MainForm();

        // Listen for "show yourself" signals from later launch attempts
        var showListener = new System.Threading.Thread(() =>
        {
            while (showSignal.WaitOne())
            {
                try { form.BeginInvoke(form.ActivateFromSecondInstance); }
                catch { /* form not ready or disposed */ }
            }
        }) { IsBackground = true };
        showListener.Start();

        Application.Run(form);

        // If settings were imported, release the mutex before starting the new instance
        // so it can acquire the single-instance lock successfully.
        if (MainForm.PendingImportRestart)
        {
            try { mutex.ReleaseMutex(); } catch { }
            try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
        }
    }

    private static void RestartAfterCrash()
    {
        try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
        Application.Exit();
    }
}
