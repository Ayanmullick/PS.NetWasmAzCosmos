# PowerShell Hello World via WebAssembly

This project demonstrates recompiling basic PowerShell scripts to C# for execution in the browser using .NET WebAssembly. It serves as a minimal example of running PowerShell logic client-side without Blazor, by translating PowerShell code into C# and hosting it via WebAssembly.

The `src/` folder contains the C# entry point (generated from PowerShell) and the HTML/JS host for browser execution. Build outputs are separated into `build/` and `publish/` folders.

## Prerequisites

- .NET SDK 10 (already installed per user)
- WebAssembly build tools (install once per machine):
  ```sh
  dotnet workload install wasm-tools
  ```
- Optional: [`dotnet-serve`](https://learn.microsoft.com/dotnet/core/tools/dotnet-serve) for hosting the published bundle.

## Build

```sh
cd C:/temp/HelloWasm
dotnet publish src/HelloWasm.csproj -c Release -r browser-wasm -o publish /p:UseAppHost=false
```

This generates intermediate build assets in `build/` and places the final WebAssembly runtime bundle in `publish/` for stable hosting.

## Run locally

1. Serve the `publish/` folder with a static server. Example:
   ```sh
   dotnet serve --directory publish/wwwroot --port 5000
   ```
2. Open `http://localhost:5000` in your browser. The `<pre>` element will display output from the recompiled PowerShell script (`Hello.ps1`), executed via the WebAssembly runtime.

## Folder layout

```
HelloWasm/
├─ README.md
├─ src/
│  ├─ HelloWasm.csproj
│  ├─ Program.cs          # C# code recompiled from PowerShell
│  └─ wwwroot/
│     ├─ app.js
│     ├─ index.html
│     └─ Hello.ps1        # Original PowerShell script
├─ build/                 # Auto-generated build artifacts
└─ publish/               # Static WebAssembly assets for hosting
```

## Notes

- `Program.cs` is the C# equivalent of the PowerShell script; `Console.WriteLine` output appears in the browser.
- The JavaScript host loads the WebAssembly runtime (`dotnet.js`) and routes console output to the page.
- Deploy by uploading the `publish/` folder to any static host (e.g., GitHub Pages, Azure Static Web Apps).
