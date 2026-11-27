using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class Shop : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Pincode { get; private set; } = string.Empty;
    public string? ContactPerson { get; private set; }
    public string? MobileNumber { get; private set; }
    public string? GstNumber { get; private set; }

    /// <summary>The name on the GST registration; the trade name (<see cref="Name"/>) when not given.</summary>
    public string? LegalName { get; private set; }

    /// <summary>The GST state code of the place of business; null until a tax profile is declared.</summary>
    public string? StateCode { get; private set; }

    /// <summary>The IANA zone the shop reads its business dates in.</summary>
    public string BusinessTimeZoneId { get; private set; } = BusinessTimeZones.DefaultId;

    public TaxRegime TaxRegime { get; private set; }

    /// <summary>The series this shop numbers its documents in, unique for its registration and fiscal year.</summary>
    public string InvoiceSeriesCode { get; private set; } = InvoiceNumberFormat.DefaultSeriesCode;

    public EInvoiceStatus EInvoiceStatus { get; private set; }
    public DateTimeOffset? EInvoiceAttestedAt { get; private set; }
    public Guid? EInvoiceAttestedByUserId { get; private set; }

    /// <summary>
    /// Who owns the shop's invoice series: the registration's GSTIN, or the shop itself when it has
    /// none. Two branches under one GSTIN therefore cannot both number in the same series.
    /// </summary>
    public string SellerRegistrationKey =>
        TaxRegime is TaxRegime.Regular or TaxRegime.Composition && Gstin.Normalize(GstNumber) is { } gstin
            ? gstin
            : $"shop:{Id:N}";

    private readonly List<ShopMembership> _memberships = [];
    public IReadOnlyList<ShopMembership> Memberships => _memberships.AsReadOnly();

    public const int MaxLegalNameLength = 200;

    private Shop() { }

    public static Shop Create(
        string name,
        string address,
        string city,
        string state,
        string pincode,
        string? contactPerson,
        string? mobileNumber,
        string? gstNumber)
    {
        return new Shop
        {
            Name = name.Trim(),
            Address = address.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            Pincode = pincode.Trim(),
            ContactPerson = NormalizeOptional(contactPerson),
            MobileNumber = NormalizeOptional(mobileNumber),
            GstNumber = NormalizeOptional(gstNumber),
            BusinessTimeZoneId = BusinessTimeZones.DefaultId,
        };
    }

    public void Rename(string name)
    {
        Name = name.Trim();
    }

    /// <summary>
    /// Updates the shop's contact details. Once a tax profile is declared the GSTIN belongs to it:
    /// an absent GSTIN keeps the registered one, and a different one is refused here so it can
    /// only change through <see cref="DeclareTaxProfile"/>, which checks it against the regime.
    /// </summary>
    public ErrorOr<Success> UpdateDetails(
        string name,
        string address,
        string city,
        string state,
        string pincode,
        string? contactPerson,
        string? mobileNumber,
        string? gstNumber)
    {
        var gstin = NormalizeOptional(gstNumber);
        if (TaxRegime != TaxRegime.NotDeclared)
        {
            gstin = Gstin.Normalize(gstin) ?? GstNumber;
            if (!string.Equals(gstin, GstNumber, StringComparison.Ordinal))
                return Errors.TaxProfile.GstinManagedByTaxProfile;
        }

        Name = name.Trim();
        Address = address.Trim();
        City = city.Trim();
        State = state.Trim();
        Pincode = pincode.Trim();
        ContactPerson = NormalizeOptional(contactPerson);
        MobileNumber = NormalizeOptional(mobileNumber);
        GstNumber = gstin;
        return Result.Success;
    }

    /// <summary>
    /// Declares the GST registration the shop issues documents under. The regime is what the owner
    /// states; the GSTIN only has to be consistent with it.
    /// </summary>
    public ErrorOr<Success> DeclareTaxProfile(
        string? legalName,
        string? stateCode,
        TaxRegime taxRegime,
        string? gstNumber,
        string? invoiceSeriesCode,
        EInvoiceStatus eInvoiceStatus,
        Guid actorUserId,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(taxRegime) || taxRegime == TaxRegime.NotDeclared)
            return Errors.TaxProfile.TaxRegimeRequired;

        var normalizedState = NormalizeOptional(stateCode);
        if (!GstStateCodes.IsKnown(normalizedState))
            return Errors.TaxProfile.StateCodeInvalid;

        var normalizedLegalName = NormalizeOptional(legalName);
        if (normalizedLegalName is { Length: > MaxLegalNameLength })
            return Errors.TaxProfile.LegalNameTooLong;

        var seriesCode = InvoiceNumberFormat.NormalizeSeriesCode(invoiceSeriesCode) ?? InvoiceNumberFormat.DefaultSeriesCode;
        if (!InvoiceNumberFormat.IsValidSeriesCode(seriesCode))
            return Errors.TaxProfile.InvoiceSeriesCodeInvalid;

        var gstin = Gstin.Normalize(gstNumber);
        if (taxRegime == TaxRegime.Unregistered)
        {
            if (gstin is not null)
                return Errors.TaxProfile.GstinNotAllowedForUnregistered;
        }
        else
        {
            if (gstin is null)
                return Errors.TaxProfile.GstinRequiredForRegime;
            if (!Gstin.IsValid(gstin))
                return Errors.TaxProfile.GstinInvalid;
            if (!string.Equals(Gstin.StateCodeOf(gstin), normalizedState, StringComparison.Ordinal))
                return Errors.TaxProfile.GstinStateMismatch;
        }

        var attestation = taxRegime == TaxRegime.Regular ? eInvoiceStatus : EInvoiceStatus.NotAttested;
        if (taxRegime == TaxRegime.Regular && (!Enum.IsDefined(attestation) || attestation == EInvoiceStatus.NotAttested))
            return Errors.TaxProfile.EInvoiceAttestationRequired;

        if (attestation != EInvoiceStatus)
        {
            EInvoiceAttestedAt = attestation == EInvoiceStatus.NotAttested ? null : now;
            EInvoiceAttestedByUserId = attestation == EInvoiceStatus.NotAttested ? null : actorUserId;
        }

        LegalName = normalizedLegalName;
        StateCode = normalizedState;
        TaxRegime = taxRegime;
        GstNumber = gstin;
        InvoiceSeriesCode = seriesCode;
        EInvoiceStatus = attestation;
        return Result.Success;
    }

    public void AddMembership(ShopMembership membership)
    {
        membership.AttachShop(this);
        _memberships.Add(membership);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
