namespace DocPrep.Application.Common;

public abstract class ApplicationError(string code, string message) : Exception(message) { public string Code { get; } = code; }
public sealed class NotFoundError(string message = "Resource not found.") : ApplicationError("resource.not_found", message);
public sealed class ForbiddenError(string message = "Access denied.") : ApplicationError("authorization.denied", message);
public sealed class ConflictError(string code, string message) : ApplicationError(code, message);
public sealed class UnauthorizedError(string code = "authentication.unauthorized", string message = "Authentication is required.") : ApplicationError(code, message);
public sealed class TooManyRequestsError(string code, string message) : ApplicationError(code, message);
public sealed class ServiceUnavailableError(string code, string message) : ApplicationError(code, message);
