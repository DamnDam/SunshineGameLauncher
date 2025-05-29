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

        private bool HandleConsoleControl(CtrlType sig)
        {
            Trace.WriteLine("Exiting game to external CTRL-C, or process kill, or shutdown");

            if (gameProcess == null)
            {
                Trace.WriteLine("No game process to clean up.");
                return true;
            }
            Trace.WriteLine($"Game process '{gameProcess.ProcessName}' with ID {gameProcess.Id} is being cleaned up.");

            // Close the main window of the game process if it exists
            if (gameProcess.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    Trace.WriteLine($"Attempting to close main window of game process '{gameProcess.ProcessName}' with ID {gameProcess.Id}");
                    if (!SetForegroundWindow(gameProcess.MainWindowHandle))
                    {
                        Trace.WriteLine($"Failed to set foreground window for process '{gameProcess.ProcessName}' with ID {gameProcess.Id}");
                    }
                    gameProcess.CloseMainWindow();
                    gameProcess.WaitForExit(4000); // Wait for the main window to close, with a timeout of 4 seconds
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to close main window: {ex.Message}");
                }
            }

            // Kill the game process if it is still running
            if (!gameProcess.HasExited)
            {
                try
                {
                    Trace.WriteLine($"Attempting to kill game process '{gameProcess.ProcessName}' with ID {gameProcess.Id}");
                    gameProcess.Kill();
                    gameProcess.WaitForExit(500);
                    Trace.WriteLine($"Game process '{gameProcess.ProcessName}' with ID {gameProcess.Id} has been killed.");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to kill game process: {ex.Message}");
                }
            }

            Trace.WriteLine("Cleanup complete");

            //shutdown right away so there are no lingering threads
            Environment.Exit(0);
            return true;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var handlerDelegate = new EventHandler(this.HandleConsoleControl);
            SetConsoleCtrlHandler(handlerDelegate, true);

            ParseAndLaunch(e);

            mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.WindowState = WindowState.Maximized;
            mainWindow.WindowStyle = WindowStyle.None;
            mainWindow.Topmost = true;
            mainWindow.Show();

            // Start process/game monitoring in the background
            _ = MonitorGameProcessAsync();
        }

        private void ParseAndLaunch(StartupEventArgs e)
        {
            if (e.Args.Length < 1)
            {
                Trace.WriteLine("No executable name provided. Exiting.");
                System.Windows.MessageBox.Show("No executable name provided. Exiting. \nUsage: SunshineLauncher.exe <exe_name> [launch_command] [timeout_seconds]", "SunshineLauncher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
                    System.Windows.MessageBox.Show($"Failed to launch game: {ex.Message}", "SunshineLauncher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
                    System.Windows.MessageBox.Show($"Invalid timeout value '{e.Args[2]}'", "SunshineLauncher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
            while (sw.Elapsed.TotalSeconds <= TimeoutSeconds)
            {
                Process? found = Process.GetProcessesByName(ExeName.Replace(".exe", "")).FirstOrDefault();
                if (found != null)
                {
                    if ((gameProcess != null && gameProcess.Id != found.Id) || gameProcess == null)
                    {
                        gameProcess = found;
                    }
                }
                await Task.Delay(500); // Non-blocking sleep
            }
            if (gameProcess == null)
            {
                Trace.WriteLine($"Game '{ExeName}' not found within {TimeoutSeconds} seconds.");
                Environment.Exit(3);
            }

            Trace.WriteLine($"Monitoring game process '{ExeName}' with ID: {gameProcess.Id}");
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
            await gameProcess.WaitForExitAsync();
            Trace.WriteLine("Exiting application.");
            await Task.Delay(3000);
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            gameProcess?.Dispose();
            base.OnExit(e);
        }
    }
}
