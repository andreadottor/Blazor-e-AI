# Blazor e AI: UI reattive tra streaming e agenti

Demo progressive per una sessione tecnica su Blazor Interactive Server:

1. `Task<T>` e risposta completa;
2. streaming con `IAsyncEnumerable<T>`;
3. pipeline Microsoft Agent Framework con aggiornamenti progressivi;
4. background processing in-memory con `Channel<T>`.

## Configurazione

I secret appartengono all'AppHost e non sono salvati nel repository:

```powershell
dotnet user-secrets set "Parameters:sql-password" "<password>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:Model" "<deployment-name>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:Endpoint" "<foundry-openai-v1-endpoint>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:ApiKey" "<api-key>" --project Dottor.BlazorAI.AppHost
```

`gpt-5.4` viene usato per chat, streaming, summary e classificazione. Per la generazione
immagini serve un deployment Foundry compatibile separato:

```powershell
dotnet user-secrets set "AI:OpenAI:ImageModel" "<image-deployment-name>" --project Dottor.BlazorAI.AppHost
```

Avvio:

```powershell
dotnet run --project Dottor.BlazorAI.AppHost
```

La Demo 4 usa volutamente channel in-memory: i job non sopravvivono al riavvio del processo.
