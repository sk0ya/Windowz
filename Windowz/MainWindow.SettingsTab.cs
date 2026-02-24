using System.Diagnostics;

namespace WindowzTabManager;

public partial class MainWindow
{
    private void OpenSettingsTab(string contentKey)
    {
        try
        {
            _pendingSettingsContentKey = contentKey;
            _viewModel.OpenContentTabCommand.Execute("SettingsHub");
            _settingsTabsPage?.SelectTab(contentKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open settings tab: {ex}");
            _viewModel.StatusMessage = "設定の表示に失敗しました";
        }
    }
}
