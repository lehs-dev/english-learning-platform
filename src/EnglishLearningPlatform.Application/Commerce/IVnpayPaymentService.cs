namespace EnglishLearningPlatform.Application.Commerce;

public sealed record VnpayIpnResult(string RspCode, string Message);

public interface IVnpayPaymentService
{
    Task<VnpayIpnResult> ConfirmVnpayAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct = default);
    // Signed Return is settled only for the authenticated owner in local mode.
    Guid? ReadVnpayReturn(IReadOnlyDictionary<string, string> query);
    Task<CheckoutCode> ConfirmVnpayReturnAsync(Guid actorId, IReadOnlyDictionary<string, string> query, CancellationToken ct = default);
}
