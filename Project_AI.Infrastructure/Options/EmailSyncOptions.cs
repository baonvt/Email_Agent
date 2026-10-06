using System.ComponentModel.DataAnnotations;

namespace Project_AI.Infrastructure.Options;

public sealed class EmailSyncOptions
{
    public const string Section = "EmailSync";
    public bool Enabled { get; set; }
    [Range(60, 3600)]
    public int IntervalSeconds { get; set; } = 300;
}
