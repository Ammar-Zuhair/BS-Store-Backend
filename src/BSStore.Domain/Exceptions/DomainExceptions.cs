namespace BSStore.Domain.Exceptions;

public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string message, string code = "DOMAIN_ERROR")
        : base(message)
    {
        Code = code;
    }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string entity, object id)
        : base($"{entity} with id '{id}' was not found.", "NOT_FOUND") { }
}

public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message, string code = "BUSINESS_RULE_VIOLATION")
        : base(message, code) { }
}

public class InvalidOrderTransitionException : DomainException
{
    public InvalidOrderTransitionException(string from, string to)
        : base($"Cannot transition order from '{from}' to '{to}'.", "INVALID_ORDER_TRANSITION") { }
}

public class InsufficientStockException : DomainException
{
    public InsufficientStockException(string productName, int requested, int available)
        : base($"Insufficient stock for '{productName}'. Requested: {requested}, Available: {available}.", "INSUFFICIENT_STOCK") { }
}

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "You are not authorized to perform this action.")
        : base(message, "UNAUTHORIZED") { }
}
