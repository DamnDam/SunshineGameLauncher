using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SunshineLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? mainWindow;
        private Process? gameProcess;
        private bool exitRequested = false;

        private string ExeName = string.Empty;
        private int TimeoutSeconds = 30;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);

        public enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private async Task<bool> WaitForGameExit(int timeoutMilliseconds)
        {
            if (gameProcess == null)
            {
                Trace.WriteLine("No game process to wait for.");
                return true;
            }

            try
            {
                gameProcess.Refresh();
                Trace.WriteLine($"Waiting for game process '{gameProcess.ProcessName}' (ID {gameProcess.Id}) to exit...");
                return await Task.Run(() =>
                {
                    return gameProcess.WaitForExit(timeoutMilliseconds);
                });

            }
            catch (InvalidOperationException)
            {
                Trace.WriteLine("Game process has already exited or is inaccessible.");
            }
            return true;
        }

        private async Task HandleClosing(CtrlType sig)
        {
            exitRequested = true;
            if (gameProcess == null)
            {
                Trace.WriteLine("No game process to clean up.");
                return;
            }

            try
            {
                gameProcess.Refresh();
                if (gameProcess.HasExited)
                {
                    Trace.WriteLine($"Game process has already exited.");
                    return;
                }

                Trace.WriteLine($"Cleaning up game process '{gameProcess.ProcessName}' (ID {gameProcess.Id})...");

                if (gameProcess.MainWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        Trace.WriteLine($"Attempting to close main window (CloseMainWindow)...");
                        // Set foreground window
                        SetForegroundWindow(gameProcess.MainWindowHandle);
                        await Task.Delay(10);
                        // Attempt to close the main window gracefully
                        gameProcess.CloseMainWindow();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to close main window: {ex.Message}");
                    }
                }

                if (await WaitForGameExit(3000))
                {
                    Trace.WriteLine($"Game process '{gameProcess.ProcessName}' (ID {gameProcess.Id}) exited gracefully.");
                }
                else
                {
                    try
                    {
                        Trace.WriteLine($"Attempting to kill process...");
                        gameProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to kill process: {ex.Message}");
                    }
                    if (await WaitForGameExit(500))
                    {
                        Trace.WriteLine($"Game process (ID {gameProcess.Id}) killed.");
                    }
                    else
                    {
                        Trace.WriteLine($"Game process (ID {gameProcess.Id}) did not exit after kill attempt.");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                Trace.WriteLine("Game process has already exited or is inaccessible.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unexpected error during cleanup: {ex.Message}");
            }
        }


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Trace.WriteLine($"Monitor PID: {Process.GetCurrentProcess().Id}");

            var handlerDelegate = new EventHandler(
                (sig) =>
                {
                    Trace.WriteLine($"Received signal: {sig}");
                    _ = HandleClosing(sig);
                    Shutdown();
                    return true; // Indicate that we handled the signal
                }
            );
            SetConsoleCtrlHandler(handlerDelegate, true);

            ParseAndLaunch(e);

            mainWindow = new MainWindow();
            Current.MainWindow = mainWindow;
            mainWindow.WindowState = WindowState.Maximized;
            mainWindow.WindowStyle = WindowStyle.None;
            mainWindow.Topmost = true;
            mainWindow.Show();
            mainWindow.Closed += async (s, args) =>
            {
                Trace.WriteLine("Event: MainWindow closed");
                await HandleClosing(CtrlType.CTRL_CLOSE_EVENT);
                Shutdown();
            };

            // Start process/game monitoring in the background
            _ = MonitorGameProcessAsync();
        }

        private void ParseAndLaunch(StartupEventArgs e)
        {
            if (e.Args.Length < 1)
            {
                Trace.WriteLine("No executable name provided. Exiting.");
                MessageBox.Show("No executable name provided. Exiting. \nUsage: SunshineLauncher.exe <exe_name> [launch_command] [timeout_seconds]", "SunshineLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
            // Parse executable name
            string exeArg = e.Args[0];
            ExeName = exeArg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeArg : exeArg + ".exe";

            // Parse launch command if provided
            if (e.Args.Length > 1 && !string.IsNullOrWhiteSpace(e.Args[1]))
            {
                var launchCommand = e.Args[1];
                Trace.WriteLine($"Launching game with command: {launchCommand}");
                try
                {
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = launchCommand,
                            UseShellExecute = true,
                        }
                    );
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to launch game with command '{launchCommand}': {ex.Message}");
                    MessageBox.Show($"Failed to launch game: {ex.Message}", "SunshineLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(2);
                }
            }

            // Parse timeout seconds if provided
            if (e.Args.Length > 2)
            {
                if (int.TryParse(e.Args[2], out int timeout) && timeout >= 0)
                {
                    TimeoutSeconds = timeout;
                }
                else
                {
                    MessageBox.Show($"Invalid timeout value '{e.Args[2]}'", "SunshineLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Environment.Exit(1);
                }
            }
        }

        private async Task MonitorGameProcessAsync()
        {
            await Task.Delay(2000);
            if (mainWindow != null)
                mainWindow.Topmost = false;
            Trace.WriteLine($"Waiting for process '{ExeName}' to start (timeout: {TimeoutSeconds}s)...");
            Stopwatch sw = Stopwatch.StartNew();

            // Game start loop
            while (sw.Elapsed.TotalSeconds <= TimeoutSeconds && !exitRequested)
            {
                Process? found = Process.GetProcessesByName(ExeName.Replace(".exe", "")).FirstOrDefault();
                if (found != null)
                {
                    if ((gameProcess != null && gameProcess.Id != found.Id) || gameProcess == null)
                    {
                        gameProcess = found;
                        Trace.WriteLine($"Found game process '{ExeName}' with ID: {gameProcess.Id}");
                        if (gameProcess.MainWindowHandle != IntPtr.Zero)
                        {
                            // Set the main window of the game process as the foreground window
                            if (!SetForegroundWindow(gameProcess.MainWindowHandle))
                            {
                                Trace.WriteLine($"Failed to set foreground window for process '{ExeName}' with ID {gameProcess.Id}");
                            }
                        }
                    }
                }
                await Task.Delay(1000);
            }
            if (gameProcess == null)
            {
                Trace.WriteLine($"Game '{ExeName}' not found within {TimeoutSeconds} seconds.");
                Environment.Exit(3);
            }

            Trace.WriteLine($"Monitoring game process '{ExeName}' with ID: {gameProcess.Id}");
            Trace.WriteLine($"Game process started at: {gameProcess.StartTime}");
            // Try to set the main window of the game process as the foreground window
            try
            {
                if (gameProcess.MainWindowHandle != IntPtr.Zero)
                {
                    // Set the main window of the game process as the foreground window
                    if (!SetForegroundWindow(gameProcess.MainWindowHandle))
                    {
                        Trace.WriteLine($"Failed to set foreground window for process '{ExeName}' with ID {gameProcess.Id}");
                    }
                }
                else
                {
                    Trace.WriteLine($"Game process '{ExeName}' does not have a main window handle.");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting foreground window: {ex.Message}");
            }

            // Monitoring loop
            while (!gameProcess.HasExited)
            {
                if (exitRequested)
                {
                    Trace.WriteLine("Exit requested, stop monitoring.");
                    return;
                }
                try
                {
                    gameProcess.Refresh();
                }
                catch (InvalidOperationException)
                {
                    Trace.WriteLine($"Game process '{ExeName}' with ID {gameProcess.Id} has exited unexpectedly.");
                    break;
                }
                await Task.Delay(1000);
            }
            Trace.WriteLine("End of monitoring.");
            await Task.Delay(3000);
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Trace.WriteLine("Exiting...");
            gameProcess?.Dispose();
            base.OnExit(e);
        }
    }
}
