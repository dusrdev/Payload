# Payload

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)

Payload is a small build-time NuGet helper for packages that need to drop bundled files into a consumer repository.

It is designed for cases where a package should bring along files such as:

- `.agents/skills/...`
- repository templates
- starter configuration files
- docs, examples, or assets that should appear in the consuming repo

The package stays deliberately narrow. It does not try to be a deployment engine, a template renderer, or a config merger. It solves one problem: package-provided content, copied during build, with simple consumer-side opt-out by tag.

## Why Payload Exists

Sometimes a NuGet package wants to ship more than assemblies.

Examples:

- a parent package wants to install one or more agent skills into `.agents/skills`
- a package wants to provide starter docs or sample files inside the consumer repo
- a tooling package wants to drop a small folder of assets into a conventional location

Payload lets the package author declare those files once, ship them inside the `.nupkg`, and have them copied into the consumer repository automatically.

## Features

- Parent packages declare bundled content with `PayloadContent`
- Consumers control copy behavior per `PackageId + Tag` with `PayloadPolicy`
- Content can be a single file or a whole directory
- Directories are copied recursively while preserving relative structure
- Copy decisions use file size plus SHA-256, not timestamps
- Parent-package `.targets` are generated automatically during pack
- Repository root detection is built in, with explicit override via `PayloadRootDirectory`
- Consumer policies support both relative and absolute destination handling through `PathKind`

## Installation

Package authors reference `Payload` from the package that will ship content:

```bash
dotnet add package Payload
```

The authoring package then declares `PayloadContent` items in its project. During pack, Payload generates the package's `build` and `buildTransitive` assets and includes the authored content under `payload/` inside the `.nupkg`.

## Authoring in a Parent Package

Declare one or more `PayloadContent` items:

```xml
<ItemGroup>
  <PayloadContent Include="content/skills/example-skill">
    <Tag>ExampleSkill</Tag>
    <TargetPath>.agents/skills/example-skill</TargetPath>
  </PayloadContent>
</ItemGroup>
```

Meaning:

- `Include`
  The local file or directory to package
- `Tag`
  The logical group name consumers can target
- `TargetPath`
  The destination path inside the consuming repository

If the source is a directory, Payload copies all files beneath it and preserves their relative layout under `TargetPath`.

## Consumer Control with `PayloadPolicy`

Consumers can opt out of specific tags:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage"
                 Tag="ExampleSkill"
                 Disable="true" />
</ItemGroup>
```

Meaning:

- `Include`
  The package id
- `Tag`
  The tag declared by the parent package
- `Disable="true"`
  Stops future synchronization for that tag

Disable is intentionally conservative:

- existing copied files are not deleted
- missing files are not restored
- future overwrites stop

## Relative and Absolute Destinations

By default, destination paths are treated as relative to the detected repository root.

If a consumer wants a specific tag to use an absolute destination instead, they can opt in explicitly:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage"
                 Tag="ExampleSkill"
                 PathKind="Absolute" />
</ItemGroup>
```

Supported values:

- `Relative`
- `Absolute`

Rules:

- `Relative` is the default
- rooted `TargetPath` values are rejected unless `PathKind="Absolute"`
- `Absolute` requires a rooted `TargetPath`
- absolute-path payloads do not depend on repository-root detection

## How Repository Root Detection Works

Relative destinations are resolved from a detected repository root.

Payload walks upward from the consuming project directory and looks for practical repository markers such as:

- `.git`
- `.hg`
- `.svn`
- `.vs`
- `.idea`
- `*.sln`
- `*.slnx`

Consumers can bypass detection completely:

```xml
<PropertyGroup>
  <PayloadRootDirectory>/path/to/repo/root</PayloadRootDirectory>
</PropertyGroup>
```

If root detection fails for a relative payload, Payload warns and skips the copy rather than guessing.

## Copy Behavior

When copying is enabled:

- file sources copy as files
- directory sources copy recursively
- local modifications are not treated as a supported customization model
- package-provided content may overwrite local files while synchronization remains enabled

Copy decisions follow this order:

1. destination missing -> copy
2. file size differs -> copy
3. file size equal -> compare SHA-256
4. hash differs -> copy
5. hash equal -> skip

This avoids relying on modified timestamps and works for text files, binaries, docs, and assets.

## Generated Package Layout

When a parent package references Payload and packs successfully, Payload generates:

- `build/<PackageId>.targets`
- `buildTransitive/<PackageId>.targets`
- packaged authored content under `payload/...`

That means the authoring package does not need to hand-maintain its own `.targets` file just to expose the bundled content.

## Example

This repository includes an end-to-end test fixture flow:

- [src/Payload](src/Payload)
  The build package itself
- [tests/ParentPackage](tests/ParentPackage)
  A parent package fixture that ships a skill folder
- [tests/ConsumerApp](tests/ConsumerApp)
  A consumer fixture that references the parent package and can opt out via `PayloadPolicy`
