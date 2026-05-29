namespace TemplateTCPServer.SDKServer.Services
{
    /// <summary>
    /// Login/auth business logic. Shared by the SDK HTTP controllers and (potentially) by
    /// GameServer packet handlers, so the same Service -&gt; Repository -&gt; DbContext chain
    /// backs both transports. Bare-bones stub for the template.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>Returns true if the credentials match a known account. (Stub.)</summary>
        Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
    }
}
