using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent
{
    [Inject] private NavigationManager SharingNavigation { get; set; } = null!;
    private string? sharedFragment, sharedJson, sharedError;
    private bool sharedLoading;
    private SessionRecovery? sharingRecovery;

    private void InitializeSharing()
    {
        SharingNavigation.LocationChanged += SharingLocationChanged;
        ObserveShareLocation(SharingNavigation.Uri);
    }

    private void SharingLocationChanged(object? sender, LocationChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            if (! disposed)
            {
                ObserveShareLocation(args.Location);
                StateHasChanged();
            }
        });

    private void ObserveShareLocation(string location)
    {
        int start = location.IndexOf('#');
        bool recognized = start >= 0 &&
                          location.AsSpan(start).StartsWith(SessionShareCodec.Namespace, StringComparison.Ordinal);

        if (! recognized)
        {
            if (sharedFragment is not null)
            {
                sessionLoadVersion++;
            }

            sharedFragment = sharedJson = sharedError = null;
            sharedLoading = false;

            return;
        }

        // Bound before extracting or decoding an attacker-controlled fragment.
        bool tooLong = location.Length - start > SessionShareCodec.MaxUrlLength;
        string fragment = tooLong ? SessionShareCodec.Namespace : location[start..];

        if (fragment == sharedFragment)
        {
            return;
        }

        sessionLoadVersion++;
        sharedFragment = fragment;
        sharedJson = sharedError = null;
        sharedLoading = false;

        if (tooLong)
        {
            sharedError = "This shared link is too large. Request a normal session file instead.";

            return;
        }

        try
        {
            string json = SessionShareCodec.Decode(fragment);
            // Use the offline migration/converter path before enabling the offer. No enrichment.
            _ = ValidateSharedOfferAsync(json, sessionLoadVersion);
        }
        catch (InvalidOperationException ex)
        {
            sharedError = ex.Message;
        }
        catch
        {
            sharedError = "The shared session link is invalid or corrupted.";
        }
    }

    private async Task ValidateSharedOfferAsync(string json, long request)
    {
        try
        {
            await _sessionService.LoadSessionAsync(json);

            if (OwnsSessionLoad(request))
            {
                sharedJson = json;
            }
        }
        catch
        {
            if (OwnsSessionLoad(request))
            {
                sharedError =
                    "The shared session is invalid or uses an unsupported session version. Your work and saved draft are kept.";
            }
        }

        if (OwnsSessionLoad(request))
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadSharedSessionAsync()
    {
        if (sharedJson is not { } json || sharedLoading || sharingRecovery?.InspectionComplete != true)
        {
            return;
        }

        SessionLoadRequest request = BeginSessionLoad();
        sharedLoading = true;

        try
        {
            SessionState session = await _sessionService.LoadSessionAsync(json);

            if (! await RestoreSessionDataAsync(session, request))
            {
                if (OwnsSessionLoad(request))
                {
                    sharedError = "Current work changed while loading. Review the shared session and load it again.";
                }

                return;
            }
        }
        catch
        {
            if (OwnsSessionLoad(request))
            {
                sharedError =
                    "The shared session is invalid or uses an unsupported session version. Your work and saved draft are kept.";
            }
        }
        finally
        {
            if (OwnsSessionLoad(request))
            {
                sharedLoading = false;
            }
        }
    }

    private void DismissSharedSession()
    {
        sessionLoadVersion++;
        ConsumeSharedFragment();
    }

    private void ConsumeSharedFragment()
    {
        if (sharedFragment is null)
        {
            return;
        }

        sharedFragment = sharedJson = sharedError = null;
        sharedLoading = false;
        string uri = SharingNavigation.Uri;
        int start = uri.IndexOf('#');

        if (start >= 0 && uri.AsSpan(start).StartsWith(SessionShareCodec.Namespace, StringComparison.Ordinal))
        {
            SharingNavigation.NavigateTo(uri[..start], replace: true);
        }
    }
}
