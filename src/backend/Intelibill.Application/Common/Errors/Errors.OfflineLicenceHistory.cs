using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    /// <summary>
    /// Reasons an offline invoice is held for historical licence review. Each code is filed on a
    /// licence history review case without altering the stored envelope.
    /// </summary>
    public static class OfflineLicenceHistory
    {
        public const string CodePrefix = "OfflineLicenceHistory.";

        private const string ReviewMessage = "The original offline licence evidence requires review.";

        public static Error EvidenceUnsupported =>
            Error.Conflict($"{CodePrefix}EvidenceUnsupported", ReviewMessage);

        public static Error EvidenceInvalid =>
            Error.Conflict($"{CodePrefix}EvidenceInvalid", ReviewMessage);

        public static Error OutsidePermitWindow =>
            Error.Conflict($"{CodePrefix}OutsidePermitWindow", ReviewMessage);

        public static Error RestrictionConflict =>
            Error.Conflict($"{CodePrefix}RestrictionConflict", ReviewMessage);

        public static Error EvidenceMissing =>
            Error.Conflict($"{CodePrefix}EvidenceMissing", ReviewMessage);

        public static Error ConflictingReceipt =>
            Error.Conflict($"{CodePrefix}ConflictingReceipt", ReviewMessage);
    }
}
