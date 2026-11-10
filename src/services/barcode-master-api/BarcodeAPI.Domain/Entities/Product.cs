using BarcodeAPI.Domain.Common;
using System.Diagnostics.CodeAnalysis;

namespace BarcodeAPI.Domain.Entities;

public class Product : BaseEntity
{
    public required string Barcode { get; set; }
    public required string Name { get; set; }

    // Private constructor for EF
    private Product() { }

    [SetsRequiredMembers]
    public Product(string barcode, string name)
    {
        Barcode = barcode;
        Name = name;
    }
}
