using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using ArdulibsManager.Infrastructure;
using ArdulibsManager.Models;
using ArdulibsManager.Services;
using Microsoft.Win32;

namespace ArdulibsManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly RepositoryRegistryService _registry;
    private readonly GithubService _github;
    private readonly LibraryScannerService _scanner;
    private readonly LibraryInstallService _installer;
    private readonly DependencyResolverService _dependencyResolver;

    private IReadOnlyList<GithubRepository> _repositories = Array.Empty<GithubRepository>();
    private string _searchText = string.Empty;
    private string _status = "Готово";
    private bool _isBusy;
    private string _librariesPath = string.Empty;
    private string _repositoryListUrl = AppSettings.DefaultRepositoryListUrl;
    private string? _githubToken;
    private bool _checkUpdatesOnStartup = true;

    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<InstalledLibrary> InstalledLibraries { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync();
        }
    }

    public string LibrariesPath
    {
        get => _librariesPath;
        set => SetProperty(ref _librariesPath, value);
    }

    public string RepositoryListUrl
    {
        get => _repositoryListUrl;
        set => SetProperty(ref _repositoryListUrl, value);
    }

    public string? GitHubToken
    {
        get => _githubToken;
        set => SetProperty(ref _githubToken, value);
    }

    public bool CheckUpdatesOnStartup
    {
        get => _checkUpdatesOnStartup;
        set => SetProperty(ref _checkUpdatesOnStartup, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public AsyncRelayCommand InitializeCommand { get; }
    public AsyncRelayCommand RefreshRegistryCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand LoadVersionsCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand BrowseLibrariesCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public MainViewModel()
    {
        var http = new HttpClient();
        _settings = new SettingsService();
        var cache = new CacheService();
        _registry = new RepositoryRegistryService(http, cache, _settings);
        _github = new GithubService(http, _settings);
        _scanner = new LibraryScannerService();
        _installer = new LibraryInstallService(_github, _settings);
        _dependencyResolver = new DependencyResolverService(_github);

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshRegistryCommand = new AsyncRelayCommand(RefreshRegistryFromSettingsAsync);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RescanCommand = new AsyncRelayCommand(RefreshInstalledListAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        InstallCommand = new AsyncRelayCommand(async p => await InstallSelectedAsync(p as SearchResult));
        LoadVersionsCommand = new AsyncRelayCommand(async p => await LoadTagsForResultAsync(p as SearchResult));
        RemoveCommand = new AsyncRelayCommand(async p => await RemoveAsync(p as InstalledLibrary));
        UpdateCommand = new AsyncRelayCommand(async p => await UpdateAsync(p as InstalledLibrary));
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllWithUpdatesAsync);
        BrowseLibrariesCommand = new RelayCommand(BrowseLibraries);
        OpenFolderCommand = new RelayCommand(p => OpenFolder((p as InstalledLibrary)?.LocalPath ?? LibrariesPath));
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settings.LoadAsync();
            LibrariesPath = settings.LibrariesPath;
            RepositoryListUrl = string.IsNullOrWhiteSpace(settings.RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : settings.RepositoryListUrl;
            GitHubToken = settings.GitHubToken;
            CheckUpdatesOnStartup = settings.CheckUpdatesOnStartup;
            await LoadRegistryAsync(force: false);
            await ScanInstalledAsync();
            if (CheckUpdatesOnStartup)
                _ = CheckUpdatesAsync(showPopup: true);
        }
        catch (Exception ex)
        {
            Log("Ошибка запуска: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshRegistryFromSettingsAsync()
    {
        var settings = _settings.Current;
        settings.LibrariesPath = LibrariesPath;
        settings.RepositoryListUrl = string.IsNullOrWhiteSpace(RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : RepositoryListUrl.Trim();
        settings.GitHubToken = GitHubToken;
        settings.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        await _settings.SaveAsync(settings);
        await LoadRegistryAsync(force: true);
    }

    private async Task LoadRegistryAsync(bool force)
    {
        IsBusy = true;
        try
        {
            Status = "Загрузка списка репозиториев...";
            _repositories = await _registry.LoadRepositoriesAsync(force);
            Status = $"Загружено репозиториев: {_repositories.Count}";
            Log(Status);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            Status = "Ошибка загрузки registry";
            Log(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task SearchAsync()
    {
        var q = SearchText.Trim();
        SearchResults.Clear();
        if (q.Length < 3) return;

        var matches = _repositories
            .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .Select(r => new SearchResult { Repository = r })
            .ToList();

        foreach (var item in matches)
        {
            item.RefreshInstallState(FindInstalled(item.Repository));
            SearchResults.Add(item);
        }

        // Важно для лимитов GitHub API: поиск работает только по локальному registry-кешу.
        // Теги/версии загружаются лениво — только когда пользователь открывает dropdown версии
        // или нажимает установку. Если dropdown не открывали, установка берёт первый GitHub tag как latest.
        await Task.CompletedTask;
    }

    private async Task LoadTagsForResultAsync(SearchResult? result)
    {
        if (result is null) return;
        if (result.TagsLoaded || result.IsLoadingTags) return;

        result.IsLoadingTags = true;
        result.Status = "Загрузка версий...";
        try
        {
            var tags = await _github.GetTagsAsync(result.Repository);
            App.Current.Dispatcher.Invoke(() =>
            {
                result.Tags.Clear();
                foreach (var tag in tags.Take(80)) result.Tags.Add(tag);
                result.SelectedTag = result.Tags.FirstOrDefault();
                result.TagsLoaded = true;
                result.Status = result.Tags.Count == 0 ? "Теги не найдены" : null;
            });
        }
        catch (Exception ex)
        {
            result.Status = "Ошибка тегов: " + ex.Message;
        }
        finally { result.IsLoadingTags = false; }
    }

    private async Task RefreshInstalledListAsync()
    {
        await ScanInstalledAsync();
        await CheckUpdatesAsync();
    }

    private async Task ScanInstalledAsync()
    {
        Status = "Сканирование установленных библиотек...";
        InstalledLibraries.Clear();
        var libs = await _scanner.ScanAsync(LibrariesPath);
        foreach (var lib in libs) InstalledLibraries.Add(lib);
        Status = $"Установлено библиотек: {InstalledLibraries.Count}";
        foreach (var sr in SearchResults) sr.RefreshInstallState(FindInstalled(sr.Repository));
        Log(Status);
    }

    private async Task CheckUpdatesAsync(bool showPopup = false)
    {
        var updated = new List<InstalledLibrary>();

        foreach (var lib in InstalledLibraries.ToList())
        {
            if (string.IsNullOrWhiteSpace(lib.Url)) continue;
            var repo = RepositoryRegistryService.ParseGithubUrl(lib.Url!);
            if (repo is null) continue;
            lib.IsChecking = true;
            try
            {
                var latest = await _github.GetLatestTagNameAsync(repo);
                lib.LatestVersion = latest;
                lib.HasUpdate = VersionService.IsNewer(latest, lib.Version);
                lib.Status = lib.HasUpdate ? "Есть обновление" : "Актуально";
                if (lib.HasUpdate) updated.Add(lib);
            }
            catch (Exception ex)
            {
                lib.Status = "Не удалось проверить: " + ex.Message;
            }
            finally { lib.IsChecking = false; }
        }

        if (showPopup && updated.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Есть обновления для библиотек:");
            sb.AppendLine();
            foreach (var lib in updated.OrderBy(x => x.Name))
                sb.AppendLine($"• {lib.Name} {lib.Version} → {lib.LatestVersion}");
            MessageBox.Show(sb.ToString(), "Доступны обновления", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private InstalledLibrary? FindInstalled(GithubRepository repo)
    {
        return InstalledLibraries.FirstOrDefault(x =>
            x.RepositoryFullName?.Equals(repo.FullName, StringComparison.OrdinalIgnoreCase) == true ||
            x.Url?.Contains(repo.FullName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private async Task InstallSelectedAsync(SearchResult? result)
    {
        if (result is null) return;
        if (!result.TagsLoaded)
            await LoadTagsForResultAsync(result);
        if (result.SelectedTag is null)
        {
            Log("Установка отменена: версии не найдены для " + result.Repository.FullName);
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(Log);
            var dependencyPlan = await _dependencyResolver.BuildInstallPlanAsync(result.Repository, _repositories, InstalledLibraries.ToList());
            if (dependencyPlan.Count > 0)
            {
                var confirm = ConfirmDependencies(dependencyPlan);
                if (confirm != MessageBoxResult.Yes)
                {
                    Log("Установка отменена пользователем: зависимости не подтверждены");
                    return;
                }

                await InstallDependencyPlanAsync(dependencyPlan, progress);
            }

            await _installer.InstallAsync(result.Repository, result.SelectedTag.Name, LibrariesPath, progress, result.InstalledLibrary?.LocalPath);
            await ScanInstalledAsync();
            _ = CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка установки: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка установки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private MessageBoxResult ConfirmDependencies(IReadOnlyList<DependencyPlanItem> dependencyPlan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Для работы библиотеки требуются зависимости:");
        sb.AppendLine();
        foreach (var item in dependencyPlan)
            sb.AppendLine("• " + item.DisplayText);
        sb.AppendLine();
        sb.AppendLine("Установить/обновить зависимости до latest и продолжить?");

        return MessageBox.Show(sb.ToString(), "Зависимости", MessageBoxButton.YesNo, MessageBoxImage.Information);
    }

    private async Task InstallDependencyPlanAsync(IReadOnlyList<DependencyPlanItem> dependencyPlan, IProgress<string> progress)
    {
        foreach (var item in dependencyPlan)
        {
            progress.Report($"Зависимость: {item.Action} {item.Repository.FullName} {item.LatestTag}");
            await _installer.InstallAsync(
                item.Repository,
                item.LatestTag,
                LibrariesPath,
                progress,
                item.InstalledLibrary?.LocalPath);
        }
    }

    private async Task UpdateAsync(InstalledLibrary? lib)
    {
        if (lib is null || string.IsNullOrWhiteSpace(lib.Url)) return;
        var repo = RepositoryRegistryService.ParseGithubUrl(lib.Url!);
        if (repo is null) return;
        var tag = lib.LatestVersion ?? await _github.GetLatestTagNameAsync(repo);
        if (string.IsNullOrWhiteSpace(tag)) return;
        IsBusy = true;
        try
        {
            await _installer.InstallAsync(repo, tag, LibrariesPath, new Progress<string>(Log), lib.LocalPath);
            await ScanInstalledAsync();
            _ = CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка обновления: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task UpdateAllWithUpdatesAsync()
    {
        var libs = InstalledLibraries.Where(x => x.HasUpdate && !string.IsNullOrWhiteSpace(x.Url)).ToList();
        if (libs.Count == 0)
        {
            MessageBox.Show("Библиотек с обновлениями не найдено.", "Обновление библиотек", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Обновить библиотеки с доступными обновлениями?\nКоличество: {libs.Count}", "Обновление библиотек", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            foreach (var lib in libs)
            {
                var repo = RepositoryRegistryService.ParseGithubUrl(lib.Url!);
                if (repo is null)
                {
                    Log("Пропущено: не удалось определить GitHub repo для " + lib.Name);
                    continue;
                }

                var tag = lib.LatestVersion ?? await _github.GetLatestTagNameAsync(repo);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    Log("Пропущено: не найдена свежая версия для " + lib.Name);
                    continue;
                }

                Log($"Обновление библиотеки: {lib.Name} -> {tag}");
                await _installer.InstallAsync(repo, tag, LibrariesPath, new Progress<string>(Log), lib.LocalPath);
            }

            await ScanInstalledAsync();
            await CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка массового обновления: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка массового обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task RemoveAsync(InstalledLibrary? lib)
    {
        if (lib is null) return;
        var confirm = MessageBox.Show($"Удалить библиотеку '{lib.Name}'?\n{lib.LocalPath}", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        await _installer.RemoveAsync(lib);
        Log("Удалено: " + lib.Name);
        await ScanInstalledAsync();
    }

    private async Task SaveSettingsAsync()
    {
        var settings = _settings.Current;
        settings.LibrariesPath = LibrariesPath;
        settings.RepositoryListUrl = string.IsNullOrWhiteSpace(RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : RepositoryListUrl.Trim();
        settings.GitHubToken = GitHubToken;
        settings.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        await _settings.SaveAsync(settings);
        Log("Настройки сохранены");
        await ScanInstalledAsync();
    }

    private void BrowseLibraries()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку Arduino libraries",
            InitialDirectory = Directory.Exists(LibrariesPath) ? LibrariesPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() == true)
            LibrariesPath = dialog.FolderName;
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void Log(string message)
    {
        App.Current.Dispatcher.Invoke(() => Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}"));
    }
}
