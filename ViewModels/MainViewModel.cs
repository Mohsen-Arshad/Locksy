using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Locksy.Helpers;
using Locksy.Models;
using Locksy.Services;

namespace Locksy.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _newTaskText = string.Empty;

    public ObservableCollection<TodoItem> Tasks { get; } = new();

    public string NewTaskText
    {
        get => _newTaskText;
        set
        {
            if (_newTaskText == value) return;
            _newTaskText = value;
            OnPropertyChanged();
        }
    }

    public Command AddTaskCommand { get; }
    public Command ClearCompletedCommand { get; }
    public event EventHandler? TasksChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(IEnumerable<TodoItem>? initialTasks = null)
    {
        AddTaskCommand = new Command(AddTask);
        ClearCompletedCommand = new Command(ClearCompleted);

        // SplashPage can load persisted tasks asynchronously while its animation runs.
        // The optional fallback keeps this view model usable from any other call site.
        foreach (var task in initialTasks ?? StorageHelper.Load())
        {
            AttachTask(task);
            Tasks.Add(task);
        }

        Tasks.CollectionChanged += OnTasksCollectionChanged;
    }

    private void AddTask()
    {
        var title = NewTaskText.Trim();
        if (title.Length == 0) return;

        Tasks.Add(new TodoItem { Title = title });
        NewTaskText = string.Empty;
    }

    public void EditTask(TodoItem task, string newTitle)
    {
        var title = newTitle.Trim();
        if (title.Length > 0) task.Title = title;
    }

    public void ChangeColor(TodoItem task, string colorKey) =>
        ChangeColor(task, colorKey, task.ColorPaletteKey);

    public void ChangeColor(TodoItem task, string colorKey, string paletteKey)
    {
        if (!TodoPalette.IsValidKey(colorKey) || !TodoPalette.IsValidPaletteKey(paletteKey))
            return;

        task.ColorKey = colorKey;
        task.ColorPaletteKey = paletteKey;
    }

    public void MoveTask(Guid sourceId, Guid targetId)
    {
        var sourceIndex = -1;
        var targetIndex = -1;

        for (var i = 0; i < Tasks.Count; i++)
        {
            if (Tasks[i].Id == sourceId) sourceIndex = i;
            if (Tasks[i].Id == targetId) targetIndex = i;
        }

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;
        Tasks.Move(sourceIndex, targetIndex);
    }

    public void RefreshTaskAppearances()
    {
        foreach (var task in Tasks) task.RefreshAppearance();
    }

    private void ClearCompleted()
    {
        foreach (var task in Tasks.Where(task => task.IsCompleted).ToList())
            Tasks.Remove(task);
    }

    private void AttachTask(TodoItem task)
    {
        task.DeleteCommand = new Command(() => Tasks.Remove(task));
        task.PropertyChanged -= OnTaskPropertyChanged;
        task.PropertyChanged += OnTaskPropertyChanged;
    }

    private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TodoItem task in e.OldItems)
                task.PropertyChanged -= OnTaskPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (TodoItem task in e.NewItems)
                AttachTask(task);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var task in Tasks) AttachTask(task);
        }

        StorageHelper.Save(Tasks);
        TasksChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Tasks));
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Persist and schedule wallpaper work only for real model changes. Appearance-only
        // notifications (RowColor, RowTextColor, TitleDecorations) must not trigger I/O or
        // a new wallpaper render; RefreshAppearance can raise several of those at once.
        if (e.PropertyName != nameof(TodoItem.Title) &&
            e.PropertyName != nameof(TodoItem.IsCompleted) &&
            e.PropertyName != nameof(TodoItem.ColorKey) &&
            e.PropertyName != nameof(TodoItem.ColorPaletteKey) &&
            e.PropertyName != nameof(TodoItem.IsReminderEnabled) &&
            e.PropertyName != nameof(TodoItem.ReminderAt))
            return;

        StorageHelper.Save(Tasks);
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
