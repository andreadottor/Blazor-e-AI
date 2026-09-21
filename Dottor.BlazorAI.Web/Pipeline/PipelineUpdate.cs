namespace Dottor.BlazorAI.Web.Pipeline;

public sealed record PipelineUpdate(
    string Step,
    PipelineUpdateStatus Status,
    string? Message = null,
    string? Summary = null,
    string? Category = null,
    bool ImageAvailable = false,
    string? ImagePrompt = null,
    string? RequestId = null);

public sealed record ImageApprovalResponse(bool Approved, string? Prompt = null);

public enum PipelineUpdateStatus
{
    Started,
    Progress,
    Completed,
    WaitingForApproval,
    Rejected,
    Cancelled,
    Failed
}
