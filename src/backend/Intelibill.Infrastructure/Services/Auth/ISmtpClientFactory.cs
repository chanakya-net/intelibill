namespace Intelibill.Infrastructure.Services.Auth;

internal interface ISmtpClientFactory
{
    ISmtpClient Create();
}
