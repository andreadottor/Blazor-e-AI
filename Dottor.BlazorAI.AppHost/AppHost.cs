var builder = DistributedApplication.CreateBuilder(args);


IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("sql-password", secret: true, value: "P4$$w0rd");
IResourceBuilder<SqlServerServerResource> sql = builder.AddSqlServer("BlazorAi", sqlPassword)
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
IResourceBuilder<SqlServerDatabaseResource> db = sql.AddDatabase("Demo");

builder.AddProject<Projects.Dottor_BlazorAI_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
