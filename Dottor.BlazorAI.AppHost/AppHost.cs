var builder = DistributedApplication.CreateBuilder(args);


var sqlPassword = builder.AddParameter("sql-password", secret: true);
var aiModel = builder.AddParameter(
    "ai-openai-model",
    secret: true,
    value: builder.Configuration["AI:OpenAI:Model"] ?? string.Empty);
var aiEndpoint = builder.AddParameter(
    "ai-openai-endpoint",
    secret: true,
    value: builder.Configuration["AI:OpenAI:Endpoint"] ?? string.Empty);
var aiApiKey = builder.AddParameter(
    "ai-openai-api-key",
    secret: true,
    value: builder.Configuration["AI:OpenAI:ApiKey"] ?? string.Empty);
var aiImageModel = builder.AddParameter(
    "ai-openai-image-model",
    secret: true,
    value: builder.Configuration["AI:OpenAI:ImageModel"] ?? string.Empty);

var sql = builder.AddSqlServer("BlazorAi", sqlPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();
var db = sql.AddDatabase("Demo");

builder.AddProject<Projects.Dottor_BlazorAI_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(db)
    .WithEnvironment("AI__OpenAI__Model", aiModel)
    .WithEnvironment("AI__OpenAI__Endpoint", aiEndpoint)
    .WithEnvironment("AI__OpenAI__ApiKey", aiApiKey)
    .WithEnvironment("AI__OpenAI__ImageModel", aiImageModel)
    .WaitFor(db);

builder.Build().Run();
