namespace Evoque.Billing.Api.Contracts;

public sealed record AsaasCustomerResponse(
    string Id,
    string Name,
    string? TaxId,
    string? Email,
    bool HasEmailRecipient,
    bool PaymentCreatedEmailEnabled);

public sealed record AsaasCustomerListResponse(
    IReadOnlyCollection<AsaasCustomerResponse> Customers,
    bool HasMore,
    int Offset,
    int TotalCount);
