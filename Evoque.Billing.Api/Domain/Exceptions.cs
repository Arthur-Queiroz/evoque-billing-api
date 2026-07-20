namespace Evoque.Billing.Api.Domain;

public class DomainException(string message) : Exception(message);

public sealed class ValidationException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ExternalOperationNotAllowedException(string message) : DomainException(message);
