namespace SolarDesk;

public partial class MainWindow
{
    private readonly Dictionary<string, MonitorTarget> manualEdits = [];
    private bool previewWholeProfile, previewIncludesAllDisplays;
    private long controlRevision;
    private Func<IReadOnlyList<MonitorTarget>, Task<List<MonitorWriteResult>>>? diagnosticWrite;

    private void SelectProfileEditor(bool night)
    {
        settings.ManualProfileSelected = false;
        selectedNightProfile = night;
        settings.SelectedNightProfile = night;
        updatingProfileSelection = true;
        try { DayTab.IsChecked = !night; NightTab.IsChecked = night; }
        finally { updatingProfileSelection = false; }
        ApplyProfileTheme(animate: true);
        foreach (var monitor in monitors) monitor.IsNightProfile = night;
        UpdateLinkedControls();
    }

    private Task<List<MonitorWriteResult>> WriteTargetsAsync(IReadOnlyList<MonitorTarget> targets, bool force = false)
        => !diagnostic ? hardware.ApplyAsync(targets, force)
            : diagnosticWrite?.Invoke(targets) ?? Task.FromResult(targets.Select(t => new MonitorWriteResult(t.Id, true, "Demo only")).ToList());

    private void ClearManualPreview()
    {
        manualDayAmount = null;
        manualUntil = null;
        previewWholeProfile = false;
        previewIncludesAllDisplays = false;
        manualEdits.Clear();
    }

    private void CancelQueuedPreview()
    {
        controlRevision++;
        saveTimer.Stop();
        pendingSliderEdits.Clear();
        ClearManualPreview();
        pauseUntil = null;
    }

    private void RememberManualEdits(IEnumerable<MonitorTarget> targets)
    {
        foreach (var target in targets)
        {
            manualEdits.TryGetValue(target.Id, out var previous);
            manualEdits[target.Id] = new(target.Id,
                target.Brightness ?? previous?.Brightness,
                target.Contrast ?? previous?.Contrast,
                target.Saturation ?? previous?.Saturation,
                target.Advanced ?? previous?.Advanced);
        }
    }

    private void QueueSelectedProfile()
    {
        applyingSharedValues = true;
        try
        {
            foreach (var monitor in monitors)
            {
                foreach (string name in ControlNames.Where(name => SupportsControl(monitor, SelectedProperty(name))))
                    ProfileChanged(monitor, SelectedProperty(name));
                if (monitor.Profile.AdvancedManaged) ProfileChanged(monitor, SelectedProperty("Advanced"));
            }
        }
        finally { applyingSharedValues = false; }
    }

    private List<MonitorTarget> PreviewTargets(double amount)
    {
        if (manualDayAmount.HasValue && !previewWholeProfile)
            return manualEdits.Values.Where(t => monitors.Any(m => m.Id == t.Id)).ToList();
        return monitors.Where(m => (m.Enabled || previewIncludesAllDisplays) && m.CanControl).Select(m => new MonitorTarget(m.Id,
            m.HasBrightness ? SolarSchedule.Interpolate(m.NightBrightness, m.DayBrightness, amount) : null,
            m.HasContrast ? SolarSchedule.Interpolate(m.NightContrast, m.DayContrast, amount) : null,
            m.HasSaturation ? SolarSchedule.Interpolate(m.NightSaturation, m.DaySaturation, amount) : null,
            m.Profile.AdvancedManaged ? (amount >= 0.5 ? m.Profile.DayAdvanced : m.Profile.NightAdvanced) : null)).ToList();
    }
}
