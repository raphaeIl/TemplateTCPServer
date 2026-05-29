namespace TemplateTCPServer.SDKServer.Services
{
    public interface IAuthService
    {
        Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
    }
}
