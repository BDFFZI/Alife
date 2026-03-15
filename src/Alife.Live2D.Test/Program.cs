using System;
using System.Threading;
using System.Windows;
using Alife.Live2D;

namespace Alife.Live2D.Test;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("=== Live2D Interactive Example ===");
        Console.WriteLine("Starting Live2D Window...");

        MainWindow? mainWindow = null;
        var initializedEvent = new ManualResetEvent(false);

        Console.WriteLine("Initializing WPF application thread...");
        var thread = new Thread(() =>
        {
            try 
            {
                Console.WriteLine("UI Thread: Starting Application...");
                var app = new Application();
                Console.WriteLine("UI Thread: Creating MainWindow...");
                mainWindow = new MainWindow();
                Console.WriteLine("UI Thread: Showing MainWindow...");
                mainWindow.Show();
                
                // Signal that the window is ready
                initializedEvent.Set();

                Console.WriteLine("UI Thread: Running Application pump...");
                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UI Thread ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                initializedEvent.Set(); // Ensure we don't hang the main thread
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Console.WriteLine("Main Thread: Waiting for Live2D Window to initialize...");
        if (!initializedEvent.WaitOne(15000))
        {
            Console.WriteLine("ERROR: MainWindow failed to initialize within 15 seconds.");
            return;
        }

        if (mainWindow == null)
        {
            Console.WriteLine("ERROR: MainWindow is null after wait.");
            return;
        }

        Console.WriteLine("Live2D Window initialized successfully.");

        Console.WriteLine("\nAvailable Commands:");
        Console.WriteLine("  say <text>     - Make the model speak");
        Console.WriteLine("  action <name>  - Trigger an animation (e.g., 开心, 思考)");
        Console.WriteLine("  model <name>   - Switch model");
        Console.WriteLine("  exit           - Close and exit");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            input = input.Trim();
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                mainWindow.Dispatcher.Invoke(() => mainWindow.Close());
                break;
            }

            var parts = input.Split(' ', 2);
            var cmd = parts[0].ToLower();
            var arg = parts.Length > 1 ? parts[1] : "";

            switch (cmd)
            {
                case "say":
                    mainWindow.Dispatcher.Invoke(() => mainWindow.Say(arg));
                    Console.WriteLine($"Executed: Say \"{arg}\"");
                    break;
                case "action":
                    mainWindow.Dispatcher.Invoke(() => mainWindow.DoAction(arg));
                    Console.WriteLine($"Executed: Trigger action \"{arg}\"");
                    break;
                case "model":
                    mainWindow.Dispatcher.Invoke(() => mainWindow.SwitchModel(arg));
                    Console.WriteLine($"Executed: Switch model to \"{arg}\"");
                    break;
                default:
                    Console.WriteLine("Unknown command. Try: say, action, model, exit");
                    break;
            }
        }

        Console.WriteLine("Exiting...");
    }
}
