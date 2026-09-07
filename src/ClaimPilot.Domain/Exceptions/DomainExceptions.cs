namespace ClaimPilot.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string message) : base(message) { }
}

public sealed class InsufficientInformationException : DomainException
{
    public InsufficientInformationException(string message) : base(message) { }
}

public sealed class PolicyVersionNotFoundException : DomainException
{
    public PolicyVersionNotFoundException(string policyNumber, DateTime incidentDate)
        : base($"No policy version found for policy '{policyNumber}' applicable on {incidentDate:yyyy-MM-dd}.") { }
}

public sealed class ValidationException : DomainException
{
    public ValidationException(string message) : base(message) { }
}

public sealed class GatedWriteException : DomainException
{
    public GatedWriteException(string message) : base(message) { }
}