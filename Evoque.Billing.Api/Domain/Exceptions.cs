namespace Evoque.Billing.Api.Domain;

public class DomainException(string message) : Exception(message);

public sealed class ValidationException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ExternalOperationNotAllowedException(string message) : DomainException(message);

public sealed class EvoSaleLookupException(int saleId, int statusCode)
    : DomainException($"Não foi possível consultar a venda {saleId} no Evo (HTTP {statusCode}).")
{
    public int SaleId { get; } = saleId;

    public int StatusCode { get; } = statusCode;
}
