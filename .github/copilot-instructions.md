---
description: Custom instructions for a PowerShell-to-C# WebAssembly project
applyTo: '**'
---

This repository contains a basic PowerShell project recompiled to C# for browser execution via .NET WebAssembly. The core component is the PowerShell-to-C# compiler (`CompilePowerShell.ps1`), which translates PowerShell scripts into C# code for WebAssembly deployment.

## Key Guidelines
- Focus on the compilation process: PowerShell scripts (e.g., `Hello.ps1`) are converted to C# at build time for client-side execution.
- Use .NET WebAssembly for browser hosting; avoid server-side assumptions.
- Keep responses concise and focused on WebAssembly, C#, and PowerShell integration.
- Prioritize the compiler's role in generating runnable C# from PowerShell.
