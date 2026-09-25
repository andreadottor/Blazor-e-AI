# Blazor e AI: UI reattive tra streaming e agenti

**Slide** per 1nn0vAI del 26.09.2026: [downlaod](1nn0vAI__Blazor_e_AI.pdf)

- Demo 1. `Task<T>` e risposta completa;
- Demo 2. streaming con `IAsyncEnumerable<T>`;
- Demo 3. pipeline Microsoft Agent Framework con aggiornamenti progressivi;
- Demo 4. background processing in-memory con `Channel<T>`.

## Configurazione

I secret appartengono all'AppHost e non sono salvati nel repository:

```powershell
dotnet user-secrets set "Parameters:sql-password" "<password>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:Model" "<deployment-name>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:Endpoint" "<foundry-openai-v1-endpoint>" --project Dottor.BlazorAI.AppHost
dotnet user-secrets set "AI:OpenAI:ApiKey" "<api-key>" --project Dottor.BlazorAI.AppHost
```

`gpt-5.6-sol` viene usato per chat, streaming, summary e classificazione. La generazione
immagini usa la MAI Images API di Foundry e un deployment separato, ad esempio `MAI-Image-2.6`:

```powershell
dotnet user-secrets set "AI:OpenAI:ImageModel" "<image-deployment-name>" --project Dottor.BlazorAI.AppHost
```

L'endpoint MAI viene ricavato dalla stessa risorsa configurata in `AI:OpenAI:Endpoint`.

Avvio:

```powershell
dotnet run --project Dottor.BlazorAI.AppHost
```

La Demo 4 usa volutamente channel in-memory: i job non sopravvivono al riavvio del processo.
