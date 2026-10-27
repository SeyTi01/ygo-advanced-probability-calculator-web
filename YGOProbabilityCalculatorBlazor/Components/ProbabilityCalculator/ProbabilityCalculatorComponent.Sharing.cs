using YGOProbabilityCalculatorBlazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [Inject] private NavigationManager SharingNavigation { get; set; } = null!;

    private string? _sharedFragment;
    private string? _sharedJson;
    private string? _sharedError;
    private bool _sharedLoading;
    private SessionRecovery? _sharingRecovery;

    private void InitializeSharing() {
        SharingNavigation.LocationChanged += SharingLocationChanged;
        ObserveShareLocation(SharingNavigation.Uri);
    }

    private void SharingLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() => {
            if (!_disposed) {
                ObserveShareLocation(args.Location);
                StateHasChanged();
            }
        });

    private void ObserveShareLocation(string location) {
        int start = location.IndexOf('#');
        bool recognized = start >= 0 && location.AsSpan(start).StartsWith(SessionShareCodec.Namespace, StringComparison.Ordinal);

        if (!recognized) {
            if (_sharedFragment is not null) {
                InvalidateSessionLoad();
            }

            _sharedFragment = _sharedJson = _sharedError = null;
            _sharedLoading = false;
            return;
        }

        // Bound before extracting or decoding an attacker-controlled fragment.
        bool tooLong = location.Length - start > SessionShareCodec.MaxUrlLength;
        string fragment = tooLong ? SessionShareCodec.Namespace : location[start..];

        if (fragment == _sharedFragment) {
            return;
        }

        InvalidateSessionLoad();
        _sharedFragment = fragment;
        _sharedJson = _sharedError = null;
        _sharedLoading = false;

        if (tooLong) {
            _sharedError = UiText.Get("Ui.ThisSharedLinkIsTooLargeRequestANormalSessionFileInstead");
            return;
        }

        try {
            string json = SessionShareCodec.Decode(fragment);

            // Use the offline migration/converter path before enabling the offer. No enrichment.
            _ = ValidateSharedOfferAsync(json, _sessionLoadVersion);
        }
        catch (InvalidOperationException ex) {
            _sharedError = ex.Message;
        }
        catch {
            _sharedError = UiText.Get("Ui.TheSharedSessionLinkIsInvalidOrCorrupted");
        }
    }

    private async Task ValidateSharedOfferAsync(string json, long request) {
        try {
            await _sessionService.LoadSessionAsync(json);

            if (OwnsSessionLoad(request)) {
                _sharedJson = json;
            }
        }
        catch {
            if (OwnsSessionLoad(request)) {
                _sharedError = UiText.Get("Ui.TheSharedSessionIsInvalidOrUsesAnUnsupportedSessionVersionYourWorkAndSavedDraftAreKept");
            }
        }

        if (OwnsSessionLoad(request)) {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadSharedSessionAsync() {
        if (_sharedJson is not { } json) {
            return;
        }

        if (_sharedLoading || _sharingRecovery?.InspectionComplete != true) {
            return;
        }

        SessionLoadRequest request = BeginSessionLoad(fullSession: true);
        _sharedLoading = true;

        try {
            await BeforeSessionWorkAsync();
            SessionState session = await _sessionService.LoadSessionAsync(json);

            if (!await RestoreSessionDataAsync(session, request)) {
                if (OwnsSessionLoad(request)) {
                    _sharedError = UiText.Get("Ui.CurrentWorkChangedWhileLoadingReviewTheSharedSessionAndLoadItAgain");
                }

                return;
            }
        }
        catch {
            if (OwnsSessionLoad(request)) {
                _sharedError = UiText.Get("Ui.TheSharedSessionIsInvalidOrUsesAnUnsupportedSessionVersionYourWorkAndSavedDraftAreKept");
            }
        }
        finally {
            EndSessionLoad(request);
            if (OwnsSessionLoad(request)) {
                _sharedLoading = false;
            }
        }
    }

    private void DismissSharedSession() {
        InvalidateSessionLoad();
        ConsumeSharedFragment();
    }

    private void ConsumeSharedFragment() {
        if (_sharedFragment is null) {
            return;
        }

        _sharedFragment = _sharedJson = _sharedError = null;
        _sharedLoading = false;
        string uri = SharingNavigation.Uri;
        int start = uri.IndexOf('#');
        bool hasSharedFragment = start >= 0 && uri.AsSpan(start).StartsWith(SessionShareCodec.Namespace, StringComparison.Ordinal);

        if (hasSharedFragment) {
            SharingNavigation.NavigateTo(uri[..start], replace: true);
        }
    }
}
