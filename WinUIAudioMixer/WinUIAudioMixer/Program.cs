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
            // Set flag so Main() starts the new instance AFTER Application.Run() returns
            // and the using block releases the mutex — avoids a race where the new process
            // sees the mutex still held and exits as a "second instance".
            MainForm.PendingCrashRestart = true;
            Application.Exit();
        };

        // Catch non-UI-thread exceptions that would otherwise terminate the process
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Services.CrashLogger.Fatal(
                $"Unhandled non-UI exception (terminating={e.IsTerminating})", ex);
            if (e.IsTerminating)
            {
                // Process is terminating immediately — release the mutex now so the
                // new instance can acquire it, then start it before we disappear.
                try { mutex.ReleaseMutex(); } catch { }
                try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
            }
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

        // Release the mutex before starting the new instance so it can acquire
        // the single-instance lock. Covers both import restarts and crash restarts.
        if (MainForm.PendingImportRestart || MainForm.PendingCrashRestart)
        {
            try { mutex.ReleaseMutex(); } catch { }
            try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
        }
    }
}
