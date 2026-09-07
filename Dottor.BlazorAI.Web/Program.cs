using Dottor.BlazorAI.Web;
using Dottor.BlazorAI.Web.Background;
using Dottor.BlazorAI.Web.Components;
using Dottor.BlazorAI.Web.Data;
using Dottor.BlazorAI.Web.Pipeline;
using Dottor.BlazorAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Demo")));

var configuredApiKey = builder.Configuration["AI:OpenAI:ApiKey"];
var apiKey = string.IsNullOrWhiteSpace(configuredApiKey) ? "not-configured" : configuredApiKey;
var model = builder.Configuration["AI:OpenAI:Model"] ?? "gpt-5.4";
var configuredImageModel = builder.Configuration["AI:OpenAI:ImageModel"];
var imageModel = string.IsNullOrWhiteSpace(configuredImageModel) ? model : configuredImageModel;
var endpoint = builder.Configuration["AI:OpenAI:Endpoint"] ?? "https://example.invalid/openai/v1";
var openAI = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

builder.Services.AddSingleton<IChatClient>(openAI.GetChatClient(model).AsIChatClient());
builder.Services.AddSingleton(openAI.GetImageClient(imageModel));
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddSingleton<DocumentImageGenerator>();
builder.Services.AddScoped<DocumentPipelineService>();
builder.Services.AddSingleton<DocumentProcessingQueue>();
builder.Services.AddSingleton<DocumentUpdateHub>();
builder.Services.AddSingleton<DocumentJobService>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/documents/{id:guid}/pdf", async (Guid id, DocumentService documents, CancellationToken ct) =>
{
    var document = await documents.GetAsync(id, ct);
    return document is null
        ? Results.NotFound()
        : Results.File(document.PdfContent, document.ContentType);
});

app.MapGet("/documents/{id:guid}/image", async (Guid id, DocumentService documents, CancellationToken ct) =>
{
    var document = await documents.GetAsync(id, ct);
    return document?.GeneratedImage is null
        ? Results.NotFound()
        : Results.File(document.GeneratedImage, document.GeneratedImageContentType ?? "image/png");
});

app.MapDefaultEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.Run();
