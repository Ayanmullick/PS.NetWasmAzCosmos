---
description: Custom instructions for a PowerShell-to-C# WebAssembly project that runs Hello World and fetches Azure Cosmos DB data
applyTo: '**'
---

This repository contains a basic PowerShell project recompiled to C# for browser execution via .NET WebAssembly. The core component is the PowerShell-to-C# compiler (`CompilePowerShell.ps1`), which translates PowerShell scripts that print "Hello World" and query Azure Cosmos DB into C# code for WebAssembly deployment.

## Key Guidelines
- Focus on the compilation process: PowerShell scripts (e.g., `Hello.ps1`) are converted to C# at build time for client-side execution, including the Hello World output and the Cosmos DB data fetch.
- Use .NET WebAssembly for browser hosting; avoid server-side assumptions.
- Highlight that Azure Cosmos DB access relies on secrets injected via environment variables/build secrets; keep security considerations in mind.
- Keep responses concise and focused on WebAssembly, C#, PowerShell integration, and Cosmos DB data retrieval in the browser.
- Prioritize the compiler's role in generating runnable C# from PowerShell so both Hello World and Cosmos queries run client-side.
