namespace Dottor.BlazorAI.Web.Components.Pages;

using Dottor.BlazorAI.Web.Data;
using Dottor.BlazorAI.Web.Services;

public partial class Documents
{
    private List<DocumentListItem> documents = [];
    private string? error;
    private bool isLoading;

    private int ProcessingCount => documents.Count(x =>
        x.Status is DocumentStatus.Uploaded or DocumentStatus.Processing or DocumentStatus.Approved);

    private int WaitingForApprovalCount => documents.Count(x =>
        x.Status == DocumentStatus.WaitingForApproval);

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        error = null;

        try
        {
            documents = await DocumentService.GetDocumentsAsync();
        }
        catch
        {
            error = "Unable to load documents. Check that the database is available and try again.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private static string StatusLabel(DocumentStatus status) => status switch
    {
        DocumentStatus.Uploaded => "Uploaded",
        DocumentStatus.Processing => "Processing",
        DocumentStatus.WaitingForApproval => "Waiting for approval",
        DocumentStatus.Approved => "Approved",
        DocumentStatus.Completed => "Completed",
        DocumentStatus.Failed => "Failed",
        DocumentStatus.Rejected => "Rejected",
        DocumentStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    private static string StatusBadge(DocumentStatus status) => status switch
    {
        DocumentStatus.Uploaded => "text-bg-secondary",
        DocumentStatus.Processing or DocumentStatus.Approved => "text-bg-primary",
        DocumentStatus.WaitingForApproval => "text-bg-warning",
        DocumentStatus.Completed => "text-bg-success",
        DocumentStatus.Failed => "text-bg-danger",
        DocumentStatus.Rejected or DocumentStatus.Cancelled => "text-bg-dark",
        _ => "text-bg-secondary"
    };

    private static string ApprovalLabel(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Pending => "Pending",
        ApprovalStatus.Approved => "Approved",
        ApprovalStatus.Rejected => "Rejected",
        _ => status.ToString()
    };

    private static string ApprovalBadge(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Pending => "text-bg-warning",
        ApprovalStatus.Approved => "text-bg-success",
        ApprovalStatus.Rejected => "text-bg-dark",
        _ => "text-bg-secondary"
    };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}
