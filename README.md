# PowerShell → C# WebAssembly (inline PowerShell in HTML)

This project demonstrates recompiling inline PowerShell (from `<script type="pwsh">` blocks in `index.html`) to C# for execution in the browser using .NET WebAssembly. The sample covers multiple commands: simple output (Hello World) and an Azure Cosmos DB query over REST, showing how inline PowerShell logic can run entirely client-side—no Blazor UI—by translating PowerShell code into C# and hosting it via WebAssembly.

The `src/` folder contains the HTML/JS host and all build-time logic. Inline `<script type="pwsh">` blocks are parsed at build time by an MSBuild task defined in `src/CompilePwshScripts.csx` (powered by RoslynCodeTaskFactory and the PowerShell SDK) which generates the C# that runs in WebAssembly. `src/RestMap.Helpers.cs` provides the Cosmos REST helpers the generated code calls.

## Prerequisites

- .NET SDK 10 (already installed per user)
- WebAssembly build tools (install once per machine):
  ```sh
  dotnet workload install wasm-tools
  ```
- Optional: [`dotnet-serve`](https://learn.microsoft.com/dotnet/core/tools/dotnet-serve) for hosting the published bundle.

## Build

```pwsh
$env:ZtechCosmosPrimaryKey = '<your key>'
cd C:/temp/PsWasmApp
dotnet publish src/PsWasmApp.csproj -c Release -r browser-wasm -o publish /p:UseAppHost=false
```

This sets the Cosmos DB key the sample uses to fetch data, generates intermediate build assets in `build/`, and places the final WebAssembly runtime bundle in `publish/` for stable hosting.

## Run locally

1. Serve the `publish/` folder with a static server. Example:
   ```pwsh
   dotnet serve --directory publish/wwwroot --port 5000
   ```
2. Open `http://localhost:5000` in your browser. The page will display output from the recompiled inline PowerShell, executed via the WebAssembly runtime.

## Folder layout

```
PsWasmApp/
|- README.md
|- src/
|  |- PsWasmApp.csproj    # invokes CompilePwshScripts.csx via RoslynCodeTaskFactory
|  |- CompilePwshScripts.csx # build-time task: parse <script type="pwsh"> and emit CompiledPowerShell.g.cs
|  |- RestMap.Helpers.cs  # partial CompiledPowerShell helpers for Cosmos REST calls
|  |- Program.cs          # app entrypoint that calls CompiledPowerShell.ExecuteAsync()
|  `- wwwroot/
|     |- app.js
|     `- index.html       # Hosts inline <script type="pwsh"> blocks for compilation
|- build/                 # Auto-generated build artifacts
`- publish/               # Static WebAssembly assets for hosting
```

## Notes

- `Program.cs` is the C# equivalent of the PowerShell script; `Console.WriteLine` output appears in the browser.
- The JavaScript host loads the WebAssembly runtime (`dotnet.js`) and routes console output to the page.
- Deploy by uploading the `publish/` folder to any static host (e.g., GitHub Pages, Azure Static Web Apps).
- During build, the MSBuild target in `PsWasmApp.csproj` emits `obj/BuildSecrets.g.cs` using the `ZtechCosmosPrimaryKey` environment variable (GitHub repo variable/secret in CI, your env var locally) so the compiled assembly can read it at runtime. The same build invokes C# script file `CompilePwshScripts.csx` to transform inline PowerShell into `CompiledPowerShell.g.cs` in `\build\obj\Debug\net10.0\browser-wasm`.
- The Cosmos DB fetch runs in the browser via .NET WebAssembly; ensure the configured key grants the least privilege needed for the sample query.
