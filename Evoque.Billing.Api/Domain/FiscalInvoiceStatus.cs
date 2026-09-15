namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Estados de uma nota fiscal. Espelha os status do Asaas e acrescenta
/// <see cref="Issuing"/>, gravado antes da chamada externa para garantir
/// idempotência, e <see cref="Failed"/>, que reúne recusa da prefeitura e falha
/// da própria chamada.
/// </summary>
public enum FiscalInvoiceStatus
{
    Issuing,
    Scheduled,
    Synchronized,
    Authorized,
    CancellationRequested,
    Canceled,
    CancellationDenied,
    Failed,
}
