namespace SolarDesk;

public partial class MainWindow
{
    // A diagnostic clock exercises startup and missed transitions without touching hardware.
    private DateTimeOffset? diagnosticNow;
    private DateTimeOffset CurrentTime => diagnostic && diagnosticNow.HasValue ? diagnosticNow.Value : DateTimeOffset.UtcNow;
    private string SelectedProfileName => selectedNightProfile ? "Night" : "Day";
    private bool IsNightAppearance => selectedNightProfile;

    private DateTimeOffset? NextAutomaticTransition()
    {
        if (!settings.AutoEnabled || !settings.HasLocation) return null;
        return schedule.NextTransitionBoundary(CurrentTime, settings);
    }

    private void UpdateEditDeadline()
    {
        if (manualDayAmount.HasValue && settings.AutoEnabled) manualUntil = NextAutomaticTransition();
    }

    private void SelectScheduledEditor()
    {
        if (settings.AutoEnabled && settings.HasLocation)
            SelectProfileEditor(schedule.Evaluate(CurrentTime, settings).DayAmount < 0.5);
        UpdateScheduleDisplay();
    }

    private async Task ApplySelectedProfileAsync(bool night)
    {
        CancelQueuedPreview();
        SelectProfileEditor(night);
        manualDayAmount = night ? 0 : 1;
        manualUntil = NextAutomaticTransition();
        QueueSelectedProfile();
        previewWholeProfile = previewIncludesAllDisplays = true;
        UpdateScheduleDisplay();
        await FlushSliderEditsAsync(force: true);
    }
}
