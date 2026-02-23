using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wind.Services;

namespace Wind.Views;

public partial class WebTabControl : UserControl, IDisposable
{
    private readonly WebViewEnvironmentService _envService;
    private bool _isInitialized;
    private bool _isDisposed;

    public Guid TabId { get; }

    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? UrlChanged;
    public event EventHandler<ImageSource?>? FaviconChanged;

    public WebTabControl(Guid tabId, WebViewEnvironmentService envService)
    {
        InitializeComponent();
        TabId = tabId;
        _envService = envService;
    }

    public async Task InitializeAsync(string url)
    {
        if (_isInitialized || _isDisposed) return;

        var env = await _envService.GetEnvironmentAsync();
        await WebView.EnsureCoreWebView2Async(env);

        WebView.CoreWebView2.PermissionRequested += (s, e) =>
        {
            if (e.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.FileReadWrite)
            {
                e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
            }
            e.SavesInProfile = true;
        };

        WebView.CoreWebView2.DocumentTitleChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                TitleChanged?.Invoke(this, WebView.CoreWebView2.DocumentTitle);
            });
        };

        WebView.CoreWebView2.SourceChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                var source = WebView.CoreWebView2.Source;
                UrlTextBox.Text = source;
                UrlChanged?.Invoke(this, source);
            });
        };

        WebView.CoreWebView2.FaviconChanged += async (s, e) =>
        {
            try
            {
                var faviconUri = WebView.CoreWebView2.FaviconUri;
                if (!string.IsNullOrEmpty(faviconUri))
                {
                    using var stream = await WebView.CoreWebView2.GetFaviconAsync(
                        Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat.Png);
                    if (stream != null)
                    {
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        if (ms.Length > 0)
                        {
                            ms.Position = 0;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = ms;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                ms.Dispose();
                                FaviconChanged?.Invoke(this, bitmap);
                            });
                            return;
                        }
                        ms.Dispose();
                    }
                }
            }
            catch
            {
                // Favicon retrieval failed, ignore
            }

            Dispatcher.Invoke(() => FaviconChanged?.Invoke(this, null));
        };

        _isInitialized = true;
        UrlTextBox.Text = url;
        WebView.CoreWebView2.Navigate(url);
    }

    public void Navigate(string urlOrSearch)
    {
        if (!_isInitialized || _isDisposed) return;

        if (Uri.TryCreate(urlOrSearch, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            WebView.CoreWebView2.Navigate(urlOrSearch);
        }
        else if (urlOrSearch.Contains('.') && !urlOrSearch.Contains(' '))
        {
            var url = urlOrSearch.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? urlOrSearch
                : "https://" + urlOrSearch;
            WebView.CoreWebView2.Navigate(url);
        }
        else
        {
            WebView.CoreWebView2.Navigate(
                $"https://www.google.com/search?q={Uri.EscapeDataString(urlOrSearch)}");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized && WebView.CoreWebView2?.CanGoForward == true)
            WebView.CoreWebView2.GoForward();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
            WebView.CoreWebView2?.Reload();
    }

    private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = UrlTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                Navigate(text);
            }
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        WebView?.Dispose();
    }
}
