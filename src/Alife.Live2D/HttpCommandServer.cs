using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Alife.Live2D;

public class HttpCommandServer
{
    private readonly HttpListener _listener;
    private readonly MainWindow _window;
    private bool _isRunning;

    public HttpCommandServer(MainWindow window, int port = 5001)
    {
        _window = window;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _isRunning = true;
        _listener.Start();
        Task.Run(ListenLoop);
        Console.WriteLine("Live2D HTTP Server started.");
    }

    public void Stop()
    {
        _isRunning = false;
        _listener.Stop();
    }

    private async Task ListenLoop()
    {
        while (_isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch (Exception ex)
            {
                if (_isRunning) Console.WriteLine($"HTTP Server Error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string path = request.Url?.AbsolutePath.ToLower() ?? "";
            var query = request.QueryString;

            switch (path)
            {
                case "/say":
                    string text = query["text"] ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        await _window.Dispatcher.InvokeAsync(() => _window.Say(text));
                    }
                    break;

                case "/action":
                    string action = query["name"] ?? "";
                    if (!string.IsNullOrEmpty(action))
                    {
                        await _window.Dispatcher.InvokeAsync(() => _window.DoAction(action));
                    }
                    break;

                case "/switch":
                    string model = query["name"] ?? "";
                    if (!string.IsNullOrEmpty(model))
                    {
                        await _window.Dispatcher.InvokeAsync(() => _window.SwitchModel(model));
                    }
                    break;

                case "/thinking":
                    string stateStr = query["state"] ?? "false";
                    bool isThinking = stateStr.ToLower() == "true";
                    await _window.Dispatcher.InvokeAsync(() => _window.SetThinking(isThinking));
                    break;

                case "/health":
                    response.StatusCode = (int)HttpStatusCode.OK;
                    byte[] healthBuffer = Encoding.UTF8.GetBytes("Health OK");
                    response.ContentLength64 = healthBuffer.Length;
                    await response.OutputStream.WriteAsync(healthBuffer, 0, healthBuffer.Length);
                    response.Close();
                    return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            byte[] buffer = Encoding.UTF8.GetBytes("OK");
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            Console.WriteLine($"HandleRequest Error: {ex.Message}");
        }
        finally
        {
            response.Close();
        }
    }
}
