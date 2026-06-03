using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using AutoAuction.Core.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// The dedicated, multi-select Drafts page: pick drafts and run "Draft with AI" or "Bulk edit"
/// across the whole selection. Batch AI runs sequentially, auto-applies (respecting per-draft
/// locks), and surfaces per-row state.
/// </summary>
public class DraftsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _shell;
    private readonly IDraftService _draftService;
    private readonly IListingGenerator _generator;
    private readonly ICategoryService _categories;

    private CancellationTokenSource? _batchCts;

    public ObservableCollection<DraftRowViewModel> Drafts { get; } = new();

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; }
    public ReactiveCommand<DraftRowViewModel, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> DiscardSelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchDraftWithAiCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelBatchCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenBulkEditCommand { get; }

    public DraftsViewModel(
        MainWindowViewModel shell,
        IDraftService draftService,
        IListingGenerator generator,
        ICategoryService categories)
    {
        _shell = shell;
        _draftService = draftService;
        _generator = generator;
        _categories = categories;

        var canActOnSelection = this.WhenAnyValue(
            x => x.SelectedCount, x => x.IsBatchRunning,
            (count, running) => count > 0 && !running);
        var whileRunning = this.WhenAnyValue(x => x.IsBatchRunning);

        RefreshCommand = ReactiveCommand.Create(RefreshDrafts);
        SelectAllCommand = ReactiveCommand.Create(() => SetAllSelected(true));
        ClearSelectionCommand = ReactiveCommand.Create(() => SetAllSelected(false));
        OpenCommand = ReactiveCommand.Create<DraftRowViewModel>(Open);
        DiscardSelectedCommand = ReactiveCommand.Create(DiscardSelected, canActOnSelection);
        BatchDraftWithAiCommand = ReactiveCommand.CreateFromTask(BatchDraftWithAiAsync, canActOnSelection);
        CancelBatchCommand = ReactiveCommand.Create(CancelBatch, whileRunning);
        OpenBulkEditCommand = ReactiveCommand.Create(OpenBulkEdit, canActOnSelection);

        RefreshDrafts();
    }

    // --- selection -----------------------------------------------------------------------------

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedCount, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(BatchButtonText));
            this.RaisePropertyChanged(nameof(BulkButtonText));
            this.RaisePropertyChanged(nameof(DiscardButtonText));
        }
    }

    public bool HasSelection => SelectedCount > 0;
    public string BatchButtonText => $"Draft with AI ({SelectedCount})";
    public string BulkButtonText => $"Bulk edit ({SelectedCount})";
    public string DiscardButtonText => $"Discard ({SelectedCount})";

    private IEnumerable<DraftRowViewModel> SelectedRows => Drafts.Where(d => d.IsSelected);

    private void SetAllSelected(bool value)
    {
        foreach (var row in Drafts)
            row.IsSelected = value;
    }

    private void RecomputeSelection() => SelectedCount = Drafts.Count(d => d.IsSelected);

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DraftRowViewModel.IsSelected))
            RecomputeSelection();
    }

    // --- batch state ---------------------------------------------------------------------------

    private bool _isBatchRunning;
    public bool IsBatchRunning
    {
        get => _isBatchRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBatchRunning, value);
            this.RaisePropertyChanged(nameof(ShowBatchLog));
        }
    }

    /// <summary>Live, newest-first lines emitted during a batch AI run.</summary>
    public ObservableCollection<string> BatchLog { get; } = new();
    public bool ShowBatchLog => IsBatchRunning || BatchLog.Count > 0;

    private void Log(string line)
    {
        BatchLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        this.RaisePropertyChanged(nameof(ShowBatchLog));
    }

    // --- bulk edit overlay ---------------------------------------------------------------------

    private BulkEditViewModel? _bulkEdit;
    public BulkEditViewModel? BulkEdit
    {
        get => _bulkEdit;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bulkEdit, value);
            this.RaisePropertyChanged(nameof(ShowBulkEdit));
        }
    }

    public bool ShowBulkEdit => _bulkEdit is not null;

    private void OpenBulkEdit()
    {
        var targets = SelectedRows.ToList();
        if (targets.Count == 0)
            return;
        BulkEdit = new BulkEditViewModel(_draftService, _categories, targets, OnBulkClosed);
    }

    private void OnBulkClosed(int appliedCount)
    {
        BulkEdit = null;
        if (appliedCount > 0)
            _shell.StatusMessage = $"Bulk settings applied to {appliedCount} draft(s)";
    }

    // --- actions -------------------------------------------------------------------------------

    public void RefreshDrafts()
    {
        foreach (var row in Drafts)
            row.PropertyChanged -= OnRowChanged;
        Drafts.Clear();

        foreach (var draft in _draftService.GetDrafts())
        {
            var row = new DraftRowViewModel(draft);
            row.PropertyChanged += OnRowChanged;
            Drafts.Add(row);
        }

        RecomputeSelection();
    }

    private void Open(DraftRowViewModel? row)
    {
        if (row is not null)
            _shell.NavigateToDraft(row.Draft);
    }

    private void DiscardSelected()
    {
        var targets = SelectedRows.ToList();
        foreach (var row in targets)
        {
            _draftService.DiscardDraft(row.FolderPath);
            row.PropertyChanged -= OnRowChanged;
            Drafts.Remove(row);
        }

        RecomputeSelection();
        _shell.StatusMessage = $"Discarded {targets.Count} draft(s); photos returned to inbox";
    }

    private void CancelBatch() => _batchCts?.Cancel();

    private async Task BatchDraftWithAiAsync()
    {
        var targets = SelectedRows.ToList();
        if (targets.Count == 0)
            return;

        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;
        IsBatchRunning = true;
        BatchLog.Clear();
        foreach (var row in targets)
            row.SetState(DraftProcessingState.Queued);

        var done = 0;
        var failed = 0;
        try
        {
            foreach (var row in targets)
            {
                if (ct.IsCancellationRequested)
                {
                    row.SetState(DraftProcessingState.Idle, string.Empty);
                    continue;
                }

                row.SetState(DraftProcessingState.Working);
                Log($"{row.DisplayTitle}: generating…");

                try
                {
                    var progress = new Progress<string>(l => Log($"   {l}"));
                    var result = await _generator.GenerateAsync(
                        new DraftFolder(row.FolderPath, row.Listing), progress, ct);

                    if (result.Ok && result.Fields is not null)
                    {
                        ListingFieldApplier.Apply(row.Listing, result.Fields, respectLocks: true);
                        _draftService.SaveListing(row.FolderPath, row.Listing);
                        row.Refresh(new DraftFolder(row.FolderPath, row.Listing));
                        row.SetState(DraftProcessingState.Done,
                            row.LockSummary.Length > 0 ? $"Done · kept {row.LockSummary}" : "Done");
                        done++;
                        Log($"{row.DisplayTitle}: done");
                    }
                    else
                    {
                        row.SetState(DraftProcessingState.Failed, result.Error ?? "AI generation failed");
                        failed++;
                        Log($"{row.DisplayTitle}: {result.Error}");
                    }
                }
                catch (OperationCanceledException)
                {
                    row.SetState(DraftProcessingState.Idle, string.Empty);
                }
                catch (Exception ex)
                {
                    row.SetState(DraftProcessingState.Failed, ex.Message);
                    failed++;
                    Log($"{row.DisplayTitle}: ERROR {ex.Message}");
                }
            }

            _shell.StatusMessage = ct.IsCancellationRequested
                ? $"Batch cancelled — {done} done, {failed} failed"
                : $"Batch AI complete — {done} done, {failed} failed";
        }
        finally
        {
            IsBatchRunning = false;
            _batchCts = null;
        }
    }
}
