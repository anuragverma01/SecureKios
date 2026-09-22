using SecureKiosk.Core.Models;

namespace SecureKiosk.Core.Interfaces;

public interface IExitAuthorizationService
{
    Task<ExitAuthorizationResult> AuthorizeAsync(string code, CancellationToken cancellationToken = default);
}
