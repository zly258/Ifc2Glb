using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ifc2Glb;

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
