using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Wind.Services;

public class WebViewEnvironmentService
{
    private CoreWebView2Environment? _environment;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment != null) return _environment;

        await _initLock.WaitAsync();
        try
        {
            if (_environment != null) return _environment;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wind", "WebView2Data");

            _environment = await CoreWebView2Environment.CreateAsync(
                null, userDataFolder);

            return _environment;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
