using System.ComponentModel.DataAnnotations;

namespace Project_AI.API.Models.Replies;

public sealed class GenerateReplyRequest
{
    public bool Force { get; set; }
    public Guid? ExpectedVersion { get; set; }
}
public sealed class EditReplyRequest
{
    [Required] public Guid? ExpectedVersion { get; set; }
    [Required, StringLength(10000, MinimumLength = 1)] public string BodyText { get; set; } = "";
}
public sealed class SendReplyRequest
{
    [Required] public Guid? ExpectedVersion { get; set; }
    public bool ConfirmSend { get; set; }
}
