using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Locksy.Commands;
using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Abstractions;
using Microsoft.Maui.Controls;
using Locksy.ViewModels.Abstractions;

namespace Locksy.ViewModels;

/// <summary>Primary page state and task operations. Device I/O is delegated to contracts.</summary>
public sealed class MainViewModel : ObservableObject, ITaskListSource
{
    private readonly ITaskRepository _taskRepository;
    private readonly ILocalizationService _localizationService;
    private bool _isInitializing = true;
    private string _newTaskText = string.Empty;
    private WallpaperViewModel? _wallpaper;
    private SettingsViewModel? _settings;
    private ReminderViewModel? _reminders;

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = new();
    public NewTaskSettingsViewModel NewTaskSettings { get; } = new();

    public WallpaperViewModel? Wallpaper
    {
        get => _wallpaper;
        private set => SetProperty(ref _wallpaper, value);
    }

    public SettingsViewModel? Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public ReminderViewModel? Reminders
    {
        get => _reminders;
        private set => SetProperty(ref _reminders, value);
    }

    public string NewTaskText
    {
        get => _newTaskText;
        set => SetProperty(ref _newTaskText, value ?? string.Empty);
    }

    public Command AddTaskCommand { get; }
    public Command ClearCompletedCommand { get; }
    public Command<TaskItemViewModel> DeleteTaskCommand { get; }

    /// <summary>Raised after task data changes so reminder and wallpaper workflows can react.</summary>
    public event EventHandler? TasksChanged;

    public MainViewModel(ITaskRepository taskRepository, ILocalizationService localizationService)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        AddTaskCommand = new Command(AddTask);
        ClearCompletedCommand = new Command(ClearCompleted);
        DeleteTaskCommand = new Command<TaskItemViewModel>(DeleteTask);
        Tasks.CollectionChanged += OnTasksCollectionChanged;
    }

    /// <summary>Initializes the UI collection from an already-loaded snapshot where available.</summary>
    public void InitializeTasks(IEnumerable<TodoItem>? initialTasks = null)
    {
        if (!_isInitializing) return;

        foreach (var task in initialTasks ?? _taskRepository.Load())
            Tasks.Add(new TaskItemViewModel(task, _localizationService));

        _isInitializing = false;
    }

    public IReadOnlyList<TodoItem> GetTaskModels() => Tasks.Select(task => task.Model).ToArray();

    public void AttachWallpaperViewModel(WallpaperViewModel wallpaperViewModel) => Wallpaper = wallpaperViewModel;
    public void AttachSettingsViewModel(SettingsViewModel settingsViewModel) => Settings = settingsViewModel;
    public void AttachReminderViewModel(ReminderViewModel reminderViewModel) => Reminders = reminderViewModel;

    private void AddTask()
    {
        var title = NewTaskText.Trim();
        if (title.Length == 0) return;

        Tasks.Add(new TaskItemViewModel(new TodoItem { Title = title }, _localizationService));
        NewTaskText = string.Empty;
    }

    public TaskItemViewModel? AddTaskWithSettings(
        string title,
        string colorKey,
        string paletteKey,
        bool reminderEnabled,
        DateTime? reminderAt)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length == 0 ||
            !TodoPalette.IsValidKey(colorKey) ||
            !TodoPalette.IsValidPaletteKey(paletteKey))
            return null;

        var hasValidReminder = reminderEnabled && reminderAt.HasValue && reminderAt.Value > DateTime.Now;
        var model = new TodoItem
        {
            Title = normalizedTitle,
            ColorKey = colorKey,
            ColorPaletteKey = paletteKey,
            ReminderAt = hasValidReminder ? reminderAt : null,
            IsReminderEnabled = hasValidReminder
        };
        var task = new TaskItemViewModel(model, _localizationService);
        Tasks.Add(task);
        NewTaskText = string.Empty;
        return task;
    }

    public void EditTask(TaskItemViewModel task, string newTitle)
    {
        ArgumentNullException.ThrowIfNull(task);
        var title = (newTitle ?? string.Empty).Trim();
        if (title.Length > 0) task.Title = title;
    }

    public void ChangeColor(TaskItemViewModel task, string colorKey) =>
        ChangeColor(task, colorKey, task.ColorPaletteKey);

    public void ChangeColor(TaskItemViewModel task, string colorKey, string paletteKey)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!TodoPalette.IsValidKey(colorKey) || !TodoPalette.IsValidPaletteKey(paletteKey)) return;
        task.ColorKey = colorKey;
        task.ColorPaletteKey = paletteKey;
    }

    public void DeleteTask(TaskItemViewModel? task)
    {
        if (task is not null) Tasks.Remove(task);
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

    public void RefreshLocalizedTaskTitles()
    {
        foreach (var task in Tasks) task.RefreshLocalizedTitle();
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

    private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TaskItemViewModel task in e.OldItems)
                task.PropertyChanged -= OnTaskPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (TaskItemViewModel task in e.NewItems)
                task.PropertyChanged += OnTaskPropertyChanged;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var task in Tasks)
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                task.PropertyChanged += OnTaskPropertyChanged;
            }
        }

        if (_isInitializing) return;
        PersistAndNotify();
        OnPropertyChanged(nameof(Tasks));
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only persisted fields cause disk I/O and downstream reminder/wallpaper refresh.
        if (e.PropertyName != nameof(TaskItemViewModel.Title) &&
            e.PropertyName != nameof(TaskItemViewModel.IsCompleted) &&
            e.PropertyName != nameof(TaskItemViewModel.ColorKey) &&
            e.PropertyName != nameof(TaskItemViewModel.ColorPaletteKey) &&
            e.PropertyName != nameof(TaskItemViewModel.TextDirection) &&
            e.PropertyName != nameof(TaskItemViewModel.IsReminderEnabled) &&
            e.PropertyName != nameof(TaskItemViewModel.ReminderAt))
            return;

        PersistAndNotify();
    }

    private void PersistAndNotify()
    {
        _taskRepository.Save(GetTaskModels());
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }
}
