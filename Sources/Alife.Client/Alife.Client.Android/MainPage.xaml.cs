namespace Alife.Client.Android;

public class MainPage : ContentPage
{
    public MainPage()
    {
        Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView webView = new() {
            HostPage = "wwwroot/index.html"
        };
        webView.RootComponents.Add(new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent {
            Selector = "#app",
            ComponentType = typeof(Components.Routes)
        });
        Content = webView;
    }
}