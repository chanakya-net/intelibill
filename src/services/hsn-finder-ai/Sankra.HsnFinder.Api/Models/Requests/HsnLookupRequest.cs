using System.ComponentModel.DataAnnotations;

namespace HsnFinder.Api.Models.Requests;

public sealed record HsnLookupRequest(
    [param: Required] string ProductName);
