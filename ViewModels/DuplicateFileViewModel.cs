using System.ComponentModel;
using System.Runtime.CompilerServices;
using CopyFinder.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CopyFinder.ViewModels;

public sealed class DuplicateFileViewModel : INotifyPropertyChanged
{
    private static readonly Brush OddRowBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 21, 23));
    private static readonly Brush EvenRowBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 18, 20));
    private static readonly Brush SelectedRowBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 32, 44));
    private static readonly Brush RowTextDefaultBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 242, 242, 242));
    private static readonly Brush RowTextSelectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

    private bool _isOriginal;
    private bool _isSelected;
    private int _rowIndex;

    public DuplicateFileViewModel(DuplicateFile file)
    {
        GroupId = file.GroupId;
        Path = file.Path;
        Size = file.Size;
        Hash = file.Hash;
        LastWriteTime = file.LastWriteTime;
        ImageWidth = file.ImageWidth;
        ImageHeight = file.ImageHeight;
        _isOriginal = file.IsOriginal;
        _isSelected = !file.IsOriginal;
        ThumbnailSource = CreatePreviewSource(file.Path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int GroupId { get; }
    public string Path { get; }
    public long Size { get; }
    public string Hash { get; }
    public DateTime LastWriteTime { get; }
    public int? ImageWidth { get; }
    public int? ImageHeight { get; }
    public bool IsOriginal
    {
        get => _isOriginal;
        private set
        {
            if (_isOriginal == value)
            {
                return;
            }

            _isOriginal = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDuplicate));
            OnPropertyChanged(nameof(Role));
            OnPropertyChanged(nameof(RowBackground));
            OnPropertyChanged(nameof(RowTextBrush));
        }
    }

    public bool IsDuplicate => !IsOriginal;
    public string Role => IsOriginal ? "Keep" : "Duplicate";
    public string FileName => GetFileName(Path);
    public string SizeText => FormatSize(Size);
    public string LastWriteText => LastWriteTime.ToString("dd/MM/yy HH:mm");
    public string HashPreview => Hash.Length > 12 ? Hash[..12] : Hash;
    public ImageSource? ThumbnailSource { get; }
    public Brush RowBackground => IsSelected && IsDuplicate
        ? SelectedRowBrush
        : _rowIndex % 2 == 0
            ? EvenRowBrush
            : OddRowBrush;
    public Brush RowTextBrush => IsSelected && IsDuplicate ? RowTextSelectedBrush : RowTextDefaultBrush;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowBackground));
            OnPropertyChanged(nameof(RowTextBrush));
        }
    }

    public void SetRowIndex(int rowIndex)
    {
        if (_rowIndex == rowIndex)
        {
            return;
        }

        _rowIndex = rowIndex;
        OnPropertyChanged(nameof(RowBackground));
    }

    public void MarkAsOriginal()
    {
        IsOriginal = true;
        IsSelected = false;
    }

    public void MarkAsDuplicate()
    {
        IsOriginal = false;
        IsSelected = true;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string GetFileName(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private static ImageSource? CreatePreviewSource(string path)
    {
        if (IsSupportedImageFile(path))
        {
            try
            {
                return new BitmapImage(new Uri(path));
            }
            catch
            {
                return CreateBundledIconSource("file-image.png");
            }
        }

        return CreateBundledIconSource(GetIconFileName(path));
    }

    private static bool IsSupportedImageFile(string path)
    {
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" => true,
            _ => false
        };
    }

    private static string GetIconFileName(string path)
    {
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".wma" or ".aiff" or ".alac" => "file-audio.png",
            ".doc" or ".docx" or ".rtf" or ".odt" => "file-word.png",
            ".pdf" => "file-pdf.png",
            ".xls" or ".xlsx" or ".csv" or ".ods" => "file-spreadsheet.png",
            ".ppt" or ".pptx" or ".odp" => "file-presentation.png",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".m4v" or ".webm" => "file-video.png",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" => "file-archive.png",
            ".txt" or ".md" or ".log" or ".nfo" or ".ini" or ".cfg" => "file-text.png",
            ".cs" or ".js" or ".ts" or ".json" or ".xml" or ".html" or ".css" or ".ps1" or ".py" or ".sql" => "file-code.png",
            _ => "file-generic.png"
        };
    }

    private static ImageSource CreateBundledIconSource(string fileName)
    {
        return new BitmapImage(new Uri($"ms-appx:///Technification/FileIcons/{fileName}"));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
