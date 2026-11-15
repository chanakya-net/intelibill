namespace Intelibill.Shared.Security;

public interface IPasswordHashing
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
