namespace Alife.Client.Android;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView webView = new() {
            HostPage = "wwwroot/index.html"
        };
        webView.RootComponents.Add(new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent {
            Selector = "#app",
            ComponentType = typeof(Alife.Client.Components.Routes)
        });
        Content = webView;
    }
}
