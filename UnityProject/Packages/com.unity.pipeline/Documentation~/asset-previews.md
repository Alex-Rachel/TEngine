# Asset previews

The package includes an importer that renders a PNG preview of an asset, up to 512×512, with the Editor's asset preview renderer.

The importer is not attached to any file type, so it never changes how your assets import. It runs only when a tool asks for the preview of a specific asset.

The Editor caches the preview like any other import result, including on a cache server when caching is enabled. A preview is regenerated when the asset, or anything it depends on such as its textures or materials, changes.

## What gets a preview

Any asset the Editor can draw a thumbnail for, such as materials, prefabs, models, textures, and SpeedTree assets. Textures keep their aspect ratio, and are rendered at their source size when that is smaller than 512×512. When the Editor cannot draw an asset, no preview is produced.

Scenes, scripts, assembly definitions, plugins, and folders never get a preview.

## Requirements

Rendering needs a graphics device. An Editor started with `-nographics` produces no previews.
