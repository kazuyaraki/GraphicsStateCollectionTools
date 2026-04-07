# GraphicsStateCollectionTools
[日本語版READMEはこちら](README.ja.md)

Tools for analyzing and visualizing Unity Graphics State Collections.

## Overview
This package includes the following two tools:

- Graphics State Collection Viewer
  - Reads GraphicsStateCollection files and displays collected shaders, passes, and keywords.
- Shader.CreateGPUProgram Extractor
  - Extracts `Shader.CreateGPUProgram` samples from Profiler `.data` files and shows which shaders were compiled on which frames.

You can also load GraphicsStateCollection and Profiler data created in other projects.

## Supported Unity Version
- Unity 6000.0.0f1 or later

## Installation
Use Unity Package Manager and select `Add package from git URL...`, then enter:

```text
https://github.com/kazuyaraki/GraphicsStateCollectionTools.git?path=Packages/com.kazuyaraki.graphicsstatecollection-tools
```

Alternatively, add this to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.kazuyaraki.graphicsstatecollection-tools": "https://github.com/kazuyaraki/GraphicsStateCollectionTools.git?path=Packages/com.kazuyaraki.graphicsstatecollection-tools"
  }
}
```

## Usage
### Graphics State Collection Viewer
Open `Window/Analysis/GSCTools/Graphics State Collection Viewer`.
Load a file using `Open File` or drag and drop a `.graphicsstate` file into the window.

![Graphics State Collection Viewer](Screenshots/GraphicsStateCollectionViewer.png)

`Summary` shows platform and graphics API information.
Below that, shaders are listed and can be filtered by shader name, pass name, and keywords using `Shader Filter`.

If a shader with the same name exists in your local project, you can select it via `Select Shader`, and pass names are also shown.
The viewer analyzes pass-level keywords and grays out keywords that are not used by the selected pass.
Because GraphicsStateCollection collects keywords at shader scope, keywords defined in other passes may also appear.

If the shader does not exist locally, `Select Shader` is disabled and pass-based keyword gray-out is not applied.

### Shader.CreateGPUProgram Extractor
Open `Window/Analysis/GSCTools/Shader.CreateGPUProgram Extractor`.
Load a file using `Open File` or drag and drop a Profiler `.data` file into the window.

![Shader.CreateGPUProgram Extractor](Screenshots/GPUProgramExtractor.png)

`Frame Summary` shows frames where shader compilation occurred and the number of events.
Below that, shaders are listed and can be filtered by shader name, pass name, and keywords using `Shader Filter`.
`Display Grouping` lets you switch between Shader-based and Frame-based grouping.

If a shader with the same name exists in your local project, you can select it via `Select Shader`.
If the shader does not exist locally, `Select Shader` is disabled.

## License
[Unlicense](https://unlicense.org/)
