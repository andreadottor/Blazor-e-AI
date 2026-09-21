namespace Dottor.BlazorAI.Web.Components;

using Dottor.BlazorAI.Web.Pipeline;
using Microsoft.AspNetCore.Components;

public partial class PipelineResults
{
    [Parameter] public Guid? DocumentId { get; set; }
    [Parameter] public Guid? JobId { get; set; }
    [Parameter] public string? FileName { get; set; }
    [Parameter] public string? PersistentStatus { get; set; }
    [Parameter] public IReadOnlyList<PipelineUpdate> Updates { get; set; } = [];
    [Parameter] public string? Summary { get; set; }
    [Parameter] public string? Category { get; set; }
    [Parameter] public bool ImageAvailable { get; set; }
    [Parameter] public string? ImagePrompt { get; set; }
    [Parameter] public bool ApprovalPending { get; set; }
    [Parameter] public bool ApprovalResponsePending { get; set; }
    [Parameter] public EventCallback<string> OnApprove { get; set; }
    [Parameter] public EventCallback OnReject { get; set; }

    private bool isEditingPrompt;
    private string? editedPrompt;
    private string? previousPrompt;

    protected override void OnParametersSet()
    {
        if (ImagePrompt != previousPrompt)
        {
            previousPrompt = ImagePrompt;
            editedPrompt = ImagePrompt;
            isEditingPrompt = false;
        }
    }

    private void BeginEditing()
    {
        editedPrompt = ImagePrompt;
        isEditingPrompt = true;
    }

    private void CancelEditing() => isEditingPrompt = false;

    private Task ApproveAsync() => OnApprove.InvokeAsync(ImagePrompt ?? string.Empty);

    private Task ApproveEditedAsync() => OnApprove.InvokeAsync(editedPrompt ?? string.Empty);

    private Task RejectAsync() => OnReject.InvokeAsync();

    private long ImageVersion => Updates.Count;

    private string JobStatus =>
        Updates.Any(x => x.Status == PipelineUpdateStatus.Failed) ? "Non riuscito" :
        Updates.Any(x => x.Status == PipelineUpdateStatus.Cancelled) ? "Annullato" :
        Updates.Any(x => x.Status == PipelineUpdateStatus.Rejected) ? "Terminato senza immagine" :
        Updates.Any(x => x.Status == PipelineUpdateStatus.WaitingForApproval) ? "In attesa di approvazione" :
        Updates.Any(x => x.Step == "Complete" && x.Status == PipelineUpdateStatus.Completed) ? "Completato" :
        "In elaborazione";

    private static string Icon(PipelineUpdateStatus status) => status switch
    {
        PipelineUpdateStatus.Completed          => "✓",
        PipelineUpdateStatus.Failed             => "✕",
        PipelineUpdateStatus.Rejected           => "✕",
        PipelineUpdateStatus.Cancelled          => "■",
        PipelineUpdateStatus.WaitingForApproval => "⚠",
        _                                       => "●"
    };

    private static string CssClass(PipelineUpdateStatus status) => status switch
    {
        PipelineUpdateStatus.Completed          => "text-success",
        PipelineUpdateStatus.Failed             => "text-danger",
        PipelineUpdateStatus.Rejected           => "text-danger",
        PipelineUpdateStatus.Cancelled          => "text-secondary",
        PipelineUpdateStatus.WaitingForApproval => "text-warning",
        _                                       => "text-primary"
    };

    private static string StepName(string step) => step switch
    {
        "Upload"      => "PDF salvato",
        "ExtractText" => "Testo estratto",
        "Summary"     => "Riassunto",
        "Category"    => "Categorizzazione",
        "ImagePrompt" => "Prompt immagine generato",
        "Approval"    => "Approvazione umana",
        "Image"       => "Generazione immagine",
        "Rejected"    => "Generazione immagine rifiutata",
        "Complete"    => "Pipeline completata",
        _             => step
    };
}
