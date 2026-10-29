using YGOProbabilityCalculatorBlazor.Localization;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public enum SessionShareFailureReason { InvalidPayload, TooLarge, UnsupportedVersion }

public sealed class SessionShareException : InvalidOperationException {
    public SessionShareFailureReason Reason { get; }

    private SessionShareException(
        SessionShareFailureReason reason,
        string message,
        Exception? innerException = null
    ) : base(message, innerException) {
        Reason = reason;
    }

    internal static SessionShareException InvalidPayload(Exception? innerException = null) =>
        new(SessionShareFailureReason.InvalidPayload, UiText.Get("Ui.TheSharedSessionLinkIsInvalidOrCorrupted"), innerException);

    internal static SessionShareException TooLarge() =>
        new(SessionShareFailureReason.TooLarge, UiText.Get("Ui.ThisSessionIsTooLargeForAShareLinkShareANormalSessionFileInstead"));

    internal static SessionShareException UnsupportedVersion() =>
        new(SessionShareFailureReason.UnsupportedVersion, UiText.Get("Ui.UnsupportedSharedLinkFormatVersion"));
}
