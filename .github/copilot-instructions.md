---
description: Custom instructions for a PowerShell-to-C# WebAssembly project that runs Hello World and fetches Azure Cosmos DB data
applyTo: '**'
---

This repository contains a PowerShell-to-C# WebAssembly project. Inline `<script type="pwsh">` blocks in `src/wwwroot/index.html` are parsed at build time by the Roslyn task in `src/CompilePwshScripts.csx`, which emits C# (including Hello World and Cosmos DB query logic) for WebAssembly execution. Cosmos REST helpers live in `src/RestMap.Helpers.cs`.

## Key Guidelines
- Focus on the build-time compilation: PowerShell inline scripts are converted to C# by `CompilePwshScripts.csx` at build time for client-side execution (Hello World + Cosmos query).
- Use .NET WebAssembly for browser hosting; avoid server-side assumptions.
- Highlight that Azure Cosmos DB access uses REST via helpers in `RestMap.Helpers.cs` and secrets injected via env/build secrets.
- Keep responses concise and focused on WebAssembly, C#, PowerShell integration, and Cosmos DB data retrieval in the browser.
- Prioritize the compiler's role in generating runnable C# from inline PowerShell so both Hello World and Cosmos queries run client-side.
