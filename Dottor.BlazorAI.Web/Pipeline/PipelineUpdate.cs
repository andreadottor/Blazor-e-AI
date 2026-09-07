namespace Dottor.BlazorAI.Web.Pipeline;

public sealed record PipelineUpdate(
    string Step,
    PipelineUpdateStatus Status,
    string? Message = null,
    string? Summary = null,
    string? Category = null,
    bool ImageAvailable = false);

public enum PipelineUpdateStatus
{
    Started,
    Progress,
    Completed,
    Failed
}
