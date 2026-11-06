using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Payment
    {
        public static Error ReceiptNotFound =>
            Error.NotFound("Payment.ReceiptNotFound", "Payment receipt not found.");

        public static Error UserIsNotOwnerOrManager =>
            Error.Forbidden("Payment.UserIsNotOwnerOrManager", "Only an owner or manager can record or allocate payments.");

        public static Error UserIsNotOwner =>
            Error.Forbidden("Payment.UserIsNotOwner", "Only an owner can reverse a payment.");

        public static Error Busy =>
            Error.Conflict("Payment.Busy", "Another payment or return for this account is in progress. Refresh and try again.");

        public static Error AlreadyReversed =>
            Error.Conflict("Payment.AlreadyReversed", "This payment has already been reversed.");

        public static Error ReversedReceiptCannotBeAllocated =>
            Error.Conflict("Payment.ReversedReceiptCannotBeAllocated", "A reversed payment cannot be allocated.");

        public static Error AllocationsRequired =>
            Error.Validation("Payment.AllocationsRequired", "At least one allocation is required.");

        public static Error TooManyAllocations(int max) =>
            Error.Validation("Payment.TooManyAllocations", $"A payment can be allocated to at most {max} documents at once.");

        public static Error DuplicateAllocationTarget(Guid targetId) =>
            Error.Validation("Payment.DuplicateAllocationTarget", $"Document {targetId} appears more than once.");

        public static Error AllocationAmountInvalid =>
            Error.Validation("Payment.AllocationAmountInvalid", "Allocation amount must be greater than zero with at most two decimals.");

        public static Error AllocationTargetNotFound(Guid targetId) =>
            Error.NotFound("Payment.AllocationTargetNotFound", $"Document {targetId} was not found for this account.");

        public static Error AllocationExceedsOutstanding(Guid targetId, decimal outstanding) =>
            Error.Conflict("Payment.AllocationExceedsOutstanding", $"Document {targetId} has only {outstanding:0.00} outstanding.");

        public static Error AllocationExceedsReceipt(decimal unallocated) =>
            Error.Conflict("Payment.AllocationExceedsReceipt", $"Only {unallocated:0.00} of this payment is still unallocated.");

        public static Error MethodInvalid =>
            Error.Validation("Payment.MethodInvalid", "Choose how the payment was made.");

        public static Error ReferenceTooLong =>
            Error.Validation("Payment.ReferenceTooLong", "Payment reference must not exceed 80 characters.");

        public static Error BankAccountNotFound =>
            Error.NotFound("Payment.BankAccountNotFound", "Bank account not found.");

        public static Error ReversalReasonInvalid =>
            Error.Validation("Payment.ReversalReasonInvalid", "A reversal reason of 3 to 255 characters is required.");

        public static Error OperationKeyRequired =>
            Error.Validation("Payment.OperationKeyRequired", "An operation key is required.");

        public static Error PartyTypeInvalid =>
            Error.Validation("Payment.PartyTypeInvalid", "Party type must be Customer or Supplier.");

        public static Error PartyNotFound =>
            Error.NotFound("Payment.PartyNotFound", "Customer or supplier not found.");

        public static Error DocumentHasAllocations =>
            Error.Conflict("Payment.DocumentHasAllocations", "Payments are allocated to this purchase receipt. Reverse those payments first.");

        public static Error CorrectionBelowAllocated(decimal allocated) =>
            Error.Conflict("Payment.CorrectionBelowAllocated", $"Payments of {allocated:0.00} are allocated to this purchase receipt; the corrected total cannot be lower.");
    }
}
