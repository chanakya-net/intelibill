using Intelibill.Licensing.Application.Trials;

namespace Intelibill.Licensing.Infrastructure.Billing;

public interface ITrialDeliveryVerifier
{
    TrialDeliveryProof? VerifyTrialDelivery(string token);
}
