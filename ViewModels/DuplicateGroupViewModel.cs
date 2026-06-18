using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CopyFinder.ViewModels;

public sealed class DuplicateGroupViewModel : INotifyPropertyChanged
{
    public DuplicateGroupViewModel(int groupId, IEnumerable<DuplicateFileViewModel> files)
    {
        GroupId = groupId;
        Files = new ObservableCollection<DuplicateFileViewModel>(files);
        Files.CollectionChanged += Files_CollectionChanged;
        RefreshRowIndexes();

        foreach (var file in Files)
        {
            file.PropertyChanged += File_PropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int GroupId { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; }
    public int SelectedCount => Files.Count(file => file.IsSelected && file.IsDuplicate);
    public string HashPreview => Files.FirstOrDefault()?.HashPreview ?? string.Empty;
    public string HeaderText => $"Group {GroupId}";
    public string ConfidenceText => $"Same size and SHA-256 hash {HashPreview}";

    public void RemoveFile(DuplicateFileViewModel file)
    {
        Files.Remove(file);
    }

    public void SetOriginal(DuplicateFileViewModel newOriginal)
    {
        foreach (var file in Files)
        {
            if (ReferenceEquals(file, newOriginal))
            {
                file.MarkAsOriginal();
            }
            else
            {
                file.MarkAsDuplicate();
            }
        }

        NotifySummaryChanged();
        RefreshRowIndexes();
    }

    public bool HasDuplicates()
    {
        return Files.Any(file => file.IsDuplicate);
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (DuplicateFileViewModel file in e.NewItems)
            {
                file.PropertyChanged += File_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (DuplicateFileViewModel file in e.OldItems)
            {
                file.PropertyChanged -= File_PropertyChanged;
            }
        }

        NotifySummaryChanged();
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateFileViewModel.IsSelected))
        {
            NotifySummaryChanged();
        }
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void RefreshRowIndexes()
    {
        for (var i = 0; i < Files.Count; i++)
        {
            Files[i].SetRowIndex(i);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
