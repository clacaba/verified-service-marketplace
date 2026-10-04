namespace Marketplace.Api.CustomerReviews;

public enum SubmissionOutcome { Created, NotFound, Forbidden, Conflict, Invalid }

// Transport-independent outcomes; endpoints own HTTP mapping.
public sealed record SubmissionResult<T>
{
    public SubmissionOutcome Outcome { get; }
    public T? Value { get; }
    public string? Reason { get; }
    public Dictionary<string, string[]>? Errors { get; }
    private SubmissionResult(SubmissionOutcome outcome, T? value = default,
        string? reason = null, Dictionary<string, string[]>? errors = null)
    {
        Outcome = outcome;
        Value = value;
        Reason = reason;
        Errors = errors;
    }
    public static SubmissionResult<T> Created(T value) => new(SubmissionOutcome.Created, value);
    public static SubmissionResult<T> NotFound() => new(SubmissionOutcome.NotFound);
    public static SubmissionResult<T> Forbidden(string reason) => new(SubmissionOutcome.Forbidden, reason: reason);
    public static SubmissionResult<T> Conflict(string reason) => new(SubmissionOutcome.Conflict, reason: reason);
    public static SubmissionResult<T> Invalid(Dictionary<string, string[]> errors) => new(SubmissionOutcome.Invalid, errors: errors);
}
