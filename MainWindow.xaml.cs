using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Ifc2Glb;

public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellationTokenSource;
    private bool isRunning;

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        OutputDirectoryTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ifc2Glb");
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 IFC 文件",
            Filter = "IFC 文件 (*.ifc;*.ifczip)|*.ifc;*.ifczip|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var outputDirectory = GetOutputDirectory();
        foreach (var fileName in dialog.FileNames)
        {
            if (Jobs.Any(j => string.Equals(j.InputPath, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Jobs.Add(new ConversionJob
            {
                InputPath = fileName,
                OutputPath = CreateOutputPath(outputDirectory, fileName),
                Status = "等待",
                Message = string.Empty
            });
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning)
        {
            return;
        }

        var selected = JobsGrid.SelectedItems.Cast<ConversionJob>().ToList();
        foreach (var job in selected)
        {
            Jobs.Remove(job);
        }
    }

    private void ClearJobs_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning)
        {
            return;
        }

        Jobs.Clear();
        LogTextBox.Clear();
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 GLB 输出目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
            RefreshOutputPaths();
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning)
        {
            return;
        }

        if (Jobs.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "请先添加 IFC 文件。", "ifc2glb", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        RefreshOutputPaths();

        cancellationTokenSource = new CancellationTokenSource();
        SetRunning(true);
        AppendLog("开始批量转换。大文件模式：串行执行，每个 IFC 完成后立即释放资源。\r\n");

        try
        {
            foreach (var job in Jobs)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                if (!File.Exists(job.InputPath))
                {
                    job.Status = "失败";
                    job.Message = "输入文件不存在。";
                    AppendLog($"[失败] {job.InputPath} 输入文件不存在。\r\n");
                    continue;
                }

                await ConvertOneAsync(job, cancellationTokenSource.Token).ConfigureAwait(true);

                if (ForceGcCheckBox.IsChecked == true)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }

            AppendLog("批量转换完成。\r\n");
        }
        catch (OperationCanceledException)
        {
            AppendLog("转换已取消。\r\n");
            foreach (var job in Jobs.Where(j => j.Status == "转换中" || j.Status == "等待"))
            {
                job.Status = "取消";
                job.Message = "用户取消。";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[异常] {ex.Message}\r\n{ex}\r\n");
        }
        finally
        {
            cancellationTokenSource.Dispose();
            cancellationTokenSource = null;
            SetRunning(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellationTokenSource?.Cancel();
    }

    private async Task ConvertOneAsync(ConversionJob job, CancellationToken cancellationToken)
    {
        job.Status = "转换中";
        job.Progress = 0;
        job.Message = "打开 IFC";
        job.ProductCount = 0;
        job.TriangleCount = 0;

        var options = new ConversionOptions
        {
            InputPath = job.InputPath,
            OutputPath = job.OutputPath,
            IncludeSpaces = IncludeSpacesCheckBox.IsChecked == true,
            IncludeOpenings = IncludeOpeningsCheckBox.IsChecked == true,
            AddYUpRootTransform = YUpCheckBox.IsChecked == true,
            TempDirectory = Path.Combine(GetOutputDirectory(), ".ifc2glb-temp")
        };

        var progress = new Progress<ConversionProgress>(p =>
        {
            job.Progress = p.Percent;
            if (!string.IsNullOrWhiteSpace(p.Status))
            {
                job.Message = p.Status;
            }

            if (p.ProductCount >= 0)
            {
                job.ProductCount = p.ProductCount;
            }

            if (p.TriangleCount >= 0)
            {
                job.TriangleCount = p.TriangleCount;
            }

            if (!string.IsNullOrWhiteSpace(p.LogLine))
            {
                AppendLog(p.LogLine + "\r\n");
            }
        });

        var sw = Stopwatch.StartNew();
        try
        {
            AppendLog($"[开始] {job.InputPath}\r\n");
            var result = await Task.Run(() => IfcToGlbConverter.Convert(options, progress, cancellationToken), cancellationToken).ConfigureAwait(true);
            sw.Stop();

            job.Status = "完成";
            job.Progress = 100;
            job.ProductCount = result.ProductCount;
            job.TriangleCount = result.TriangleCount;
            job.Message = $"完成，用时 {sw.Elapsed:hh\\:mm\\:ss}";
            AppendLog($"[完成] {result.OutputPath}\r\n");
            AppendLog($"       构件 {result.ProductCount}，Shape {result.ShapeInstanceCount}，三角面 {result.TriangleCount}，跳过 {result.SkippedShapeCount}\r\n");
        }
        catch (OperationCanceledException)
        {
            job.Status = "取消";
            job.Message = "用户取消。";
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            job.Status = "失败";
            job.Message = ex.Message;
            AppendLog($"[失败] {job.InputPath}\r\n{ex}\r\n");
        }
    }

    private void SetRunning(bool running)
    {
        isRunning = running;
        StartButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        SettingsPanel.IsEnabled = !running;
        JobsGrid.IsEnabled = !running;
    }

    private string GetOutputDirectory()
    {
        var directory = OutputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ifc2Glb");
        }

        return Path.GetFullPath(directory);
    }

    private void RefreshOutputPaths()
    {
        var outputDirectory = GetOutputDirectory();
        foreach (var job in Jobs)
        {
            job.OutputPath = CreateOutputPath(outputDirectory, job.InputPath);
        }
    }

    private static string CreateOutputPath(string outputDirectory, string inputPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(outputDirectory, baseName + ".glb");
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText(message);
        LogTextBox.ScrollToEnd();
    }
}

public sealed class ConversionJob : INotifyPropertyChanged
{
    private string inputPath = string.Empty;
    private string outputPath = string.Empty;
    private string status = string.Empty;
    private string message = string.Empty;
    private int progress;
    private int productCount;
    private int triangleCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InputPath
    {
        get => inputPath;
        set => SetField(ref inputPath, value);
    }

    public string OutputPath
    {
        get => outputPath;
        set => SetField(ref outputPath, value);
    }

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public string Message
    {
        get => message;
        set => SetField(ref message, value);
    }

    public int Progress
    {
        get => progress;
        set
        {
            if (SetField(ref progress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => Progress + "%";

    public int ProductCount
    {
        get => productCount;
        set => SetField(ref productCount, value);
    }

    public int TriangleCount
    {
        get => triangleCount;
        set => SetField(ref triangleCount, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
