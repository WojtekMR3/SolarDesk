using System.Windows;
using System.Windows.Threading;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckReviewRegressionsAsync(Action<bool, string> check)
    {
        if (!diagnostic) throw new InvalidOperationException("Regression checks require demo mode.");
        LinkModeBox.SelectedIndex = 0; DayTab.IsChecked = true;
        CancelQueuedPreview(); settings.AutoEnabled = false;
        var originals = monitors.ToDictionary(m => m.Id, m => DisplayProfileValues.Capture(m.Profile));
        var managed = monitors.ToDictionary(m => m.Id, m => m.Profile.AdvancedManaged);
        static List<MonitorWriteResult> Success(IReadOnlyList<MonitorTarget> targets) => targets.Select(t => new MonitorWriteResult(t.Id, true, "Demo only")).ToList();
        var writes = new List<MonitorTarget>();
        try
        {
            monitors[0].SelectedBrightness = (monitors[0].SelectedBrightness + 1) % 101;
            await RestoreAsync();
            check(pendingSliderEdits.Count == 0 && !saveTimer.IsEnabled && manualDayAmount is null,
                "Restore cancels queued slider edits before restoring hardware");
            await FlushSliderEditsAsync();
            check(manualDayAmount is null && !settings.AutoEnabled,
                "A delayed save cannot reactivate a manual preview after Restore");

            monitors[0].SelectedBrightness = (monitors[0].SelectedBrightness + 1) % 101;
            await ResumeAsync();
            check(pendingSliderEdits.Count == 0 && manualDayAmount is null && settings.AutoEnabled,
                "Resume auto cancels unsent manual writes while keeping saved profile edits");
            check(selectedNightProfile == (schedule.Evaluate(DateTimeOffset.UtcNow, settings).DayAmount < 0.5),
                "Auto selects the scheduled Day/Night editor without triggering a manual override");
            CancelQueuedPreview(); settings.AutoEnabled = false;

            NightTab.IsChecked = true;
            CancelQueuedPreview();
            var otherBrightness = monitors.Skip(1).Select(m => m.CurrentBrightness).ToArray();
            diagnosticWrite = targets => { writes.AddRange(targets); return Task.FromResult(Success(targets)); };
            monitors[0].SelectedSaturation = (monitors[0].SelectedSaturation + 1) % 101;
            await FlushSliderEditsAsync(); writes.Clear();
            await CycleAsync();
            check(writes.Count == 1 && writes[0].Id == monitors[0].Id && writes[0].Saturation.HasValue
                && writes[0].Brightness is null && writes[0].Contrast is null && writes[0].Advanced is null
                && monitors.Skip(1).Select(m => m.CurrentBrightness).SequenceEqual(otherBrightness),
                "Scheduler ticks keep an Individual edit scoped to its display and edited control");
            await ManualAsync(1);
            writes.Clear(); await CycleAsync();
            check(writes.Count == monitors.Count && writes.All(t => t.Brightness.HasValue && t.Contrast.HasValue && t.Saturation.HasValue),
                "Explicit tray profile commands still apply the full profile to enabled displays");

            foreach (int mode in new[] { 0, 1, 2 })
            {
                LinkModeBox.SelectedIndex = mode; await FlushSliderEditsAsync();
                DayTab.IsChecked = true;
                var beforeSwitch = monitors.Select(m => DisplayProfileValues.Capture(m.Profile)).ToArray();
                writes.Clear(); NightTab.IsChecked = true;
                check(writes.Count == monitors.Count && writes.All(t =>
                    {
                        var monitor = monitors.First(m => m.Id == t.Id);
                        return t.Brightness == monitor.NightBrightness && t.Contrast == monitor.NightContrast && t.Saturation == monitor.NightSaturation
                            && t.Advanced == (monitor.Profile.AdvancedManaged ? monitor.Profile.NightAdvanced : null);
                    }) && monitors.Select(m => DisplayProfileValues.Capture(m.Profile)).SequenceEqual(beforeSwitch),
                    $"Night selection applies all saved controls without changing either profile in {(MonitorLinkMode)mode}");
                writes.Clear(); DayTab.IsChecked = true;
                check(writes.Count == monitors.Count && writes.All(t => t.Brightness == monitors.First(m => m.Id == t.Id).DayBrightness)
                    && !settings.AutoEnabled && manualUntil is null,
                    $"Day selection restores saved values and stays active in {(MonitorLinkMode)mode}");
            }
            LinkModeBox.SelectedIndex = 0; await FlushSliderEditsAsync();

            CancelQueuedPreview(); DayTab.IsChecked = true;
            var completion = new TaskCompletionSource<List<MonitorWriteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var batches = new List<MonitorTarget[]>();
            diagnosticWrite = async targets =>
            {
                var copy = targets.ToArray(); batches.Add(copy);
                await completion.Task;
                return Success(copy);
            };
            int dayValue = (monitors[0].DayBrightness + 1) % 101;
            int nightValue = monitors[0].NightBrightness;
            monitors[0].SelectedBrightness = dayValue;
            var outgoingWrite = FlushSliderEditsAsync();
            NightTab.IsChecked = true;
            try
            {
                check(monitors.All(m => m.IsNightProfile) && monitors[0].SelectedBrightness == nightValue
                    && batches[0].Single().Brightness == dayValue && batches[1].Length == monitors.Count
                    && batches[1][0].Brightness == nightValue && !completion.Task.IsCompleted,
                    "Day/Night editors switch immediately while the outgoing monitor write is still running");
            }
            finally { completion.TrySetResult([]); }
            await outgoingWrite;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            check(monitors[0].CurrentBrightness == nightValue && !settings.AutoEnabled && manualUntil is null && settings.SelectedNightProfile,
                "The selected Night profile wins over an older write and remains active without an expiry");
            diagnosticWrite = null;
            monitors[0].SelectedBrightness = (nightValue + 1) % 101;
            await FlushSliderEditsAsync();
            check(monitors[0].DayBrightness == dayValue && monitors[0].NightBrightness == (nightValue + 1) % 101,
                "Editing after a slow profile switch changes the newly selected profile only");

            CancelQueuedPreview(); DayTab.IsChecked = true;
            var main = monitors.First(m => m.IsPrimary);
            main.SetAdvanced(main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Sharpness).Control, 7);
            await FlushSliderEditsAsync(); CancelQueuedPreview();
            var preset = new MonitorPreset { Name = "Regression", Values = new(main.SelectedBrightness, main.SelectedContrast, main.SelectedSaturation), Advanced = main.SelectedAdvanced };
            writes.Clear();
            diagnosticWrite = targets => { writes.AddRange(targets); return Task.FromResult(Success(targets)); };
            await ApplyMonitorPresetAsync(main, preset);
            check(writes.Single().Advanced?.Sharpness == 7,
                "Applying a preset sends its extra controls even when saved profile values already match");

            CancelQueuedPreview();
            var stale = new TaskCompletionSource<List<MonitorWriteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<MonitorTarget> staleTargets = [];
            diagnosticWrite = targets => { staleTargets = targets.ToList(); return stale.Task; };
            monitors[0].SelectedBrightness = 91;
            var inFlight = FlushSliderEditsAsync();
            try
            {
                await RestoreAsync();
                int restored = monitors[0].CurrentBrightness;
                stale.TrySetResult(Success(staleTargets));
                await inFlight;
                check(monitors[0].CurrentBrightness == restored && manualDayAmount is null,
                    "An old write completion cannot overwrite the restored UI state");
            }
            finally { stale.TrySetResult(Success(staleTargets)); }

            diagnosticWrite = targets => { writes.AddRange(targets); return Task.FromResult(Success(targets)); };
            writes.Clear(); CancelQueuedPreview(); monitors.Clear();
            DayTab.IsChecked = true; NightTab.IsChecked = true;
            refreshRequested = true; await CycleAsync();
            check(monitors.Count == 3 && writes.Count == 3 && writes.All(t => t.Brightness == monitors.First(m => m.Id == t.Id).NightBrightness),
                "A profile chosen before monitor discovery applies when the displays become available");

            var previousCycle = new TaskCompletionSource<List<MonitorWriteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cycleBatches = new List<MonitorTarget[]>();
            diagnosticWrite = async targets =>
            {
                var copy = targets.ToArray(); cycleBatches.Add(copy);
                if (cycleBatches.Count == 1) await previousCycle.Task;
                return Success(copy);
            };
            var oldCycle = CycleAsync();
            try
            {
                await ResumeAsync();
                check(cycleRequested && settings.AutoEnabled, "An Auto request during a slow monitor operation is queued");
                previousCycle.TrySetResult([]); await oldCycle;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                double amount = schedule.Evaluate(DateTimeOffset.UtcNow, settings).DayAmount;
                check(cycleBatches.Count == 2 && manualDayAmount is null && cycleBatches[1].All(t =>
                    {
                        var monitor = monitors.First(m => m.Id == t.Id);
                        return t.Brightness == SolarSchedule.Interpolate(monitor.NightBrightness, monitor.DayBrightness, amount);
                    }), "Auto resumes immediately after the older operation finishes");
            }
            finally { previousCycle.TrySetResult([]); }
        }
        finally
        {
            diagnosticWrite = null;
            CancelQueuedPreview(); settings.AutoEnabled = false; DayTab.IsChecked = true;
            foreach (var monitor in monitors)
            {
                originals[monitor.Id].Apply(monitor.Profile);
                monitor.Profile.AdvancedManaged = managed[monitor.Id];
                monitor.RefreshProfileValues();
            }
            UpdateLinkedControls(); UpdateScheduleDisplay();
        }
    }
}
