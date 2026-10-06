using System.ComponentModel.DataAnnotations;

namespace Project_AI.API.Models;

public sealed class EmailListQuery
{
    [Range(1, 100_000)]
    public int Page { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
    public bool UnreadOnly { get; set; }
}
