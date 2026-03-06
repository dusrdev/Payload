# AGENTS.md

This repository contains a small build-time NuGet helper that copies files bundled inside a parent NuGet package into a consumer repository on build.

## Purpose

The package is intentionally narrow.

It should do one thing:

- let a parent package declare grouped copy instructions
- let a downstream consumer optionally disable a group by tag
- copy bundled files or directories into a detected repository root during build

Typical use cases include:

- `.agents/skills/...`
- repo templates
- docs or examples bundled with a package
- package-provided configuration starters

## Decisions already made

These are not open questions unless explicitly changed.

### Data model

Use `ItemGroup`, not `PropertyGroup`.

Parent package authoring item shape:

- item name: `PayloadContent`
- metadata:
  - `Tag`
  - `TargetPath`
  - `CopyOnBuild`

For authoring in the parent package project, the item `Include` is the local source file or directory.

At pack time, `Payload` should generate the parent package's transitive `.targets` file and pack the authored files into the `.nupkg`.

Consumer policy item shape:

- item name: `PayloadPolicy`
- metadata or attributes:
  - `Tag`
  - `CopyOnBuild`
  - `PathKind`

The item `Include` is the `PackageId`.

Consumer policies are scoped by `PackageId + Tag` if practical to implement without too much complexity.

### Grouping and atomicity

There is only one grouping key: `Tag`.

- no separate `Id`
- a tag owns a group of `{ Include -> TargetPath }`
- no partial override inside a tag
- if a parent package author wants more granularity, they must split tags themselves

Tag uniqueness is the responsibility of the parent package author.

### Copy behavior

When effective `CopyOnBuild` is `true`:

- if source is a file, copy the file
- if source is a directory, copy recursively preserving relative structure under `TargetPath`
- copied files are package-provided artifacts
- local edits are not considered a supported customization model
- while copying remains enabled, local edits may be overwritten

When effective `CopyOnBuild` is `false`:

- stop copying for that `PackageId + Tag`
- do not remove existing copied files
- do not restore missing files
- do not overwrite existing files

This is intentionally conservative so disabling does not destroy consumer files.

### Root detection

Target paths are relative to a detected customer root.

Root detection should be smart and practical, borrowing from the existing approach in Zakira.Imprint.

Consumer projects may set `PayloadRootDirectory` explicitly to bypass detection.

If root detection fails:

- warn
- skip

Do not fall back silently to some arbitrary path.

### Paths

For version one, parent package authors declare only `TargetPath`.

Consumer-side `PathKind` belongs on `PayloadPolicy`.

Current behavior:

- consumer paths are relative by default
- consumer projects may opt a tag into absolute destination handling with `PathKind="Absolute"`
- supported `PathKind` values are `Relative` and `Absolute`
- if `PathKind` is `Relative`, rooted `TargetPath` values are skipped with a warning
- if `PathKind` is `Absolute`, non-rooted `TargetPath` values are skipped with a warning

### File comparison

Do not rely on modified date as the main truth.

Preferred behavior:

1. destination missing -> copy
2. file size differs -> copy
3. file size equal -> compare content hash
4. hash differs -> copy
5. hash equal -> skip

Use SHA-256.

Reason:

- works for markdown, binaries, and assets
- deterministic
- avoids timestamp weirdness

### Cleanup

No `.gitignore` support in version one.

No automatic removal on disable.

`CopyOnBuild="false"` means only: stop future synchronization.

`CopyOnBuild` resolution order:

1. consumer `PayloadPolicy`, if specified
2. otherwise parent `PayloadContent`, if specified
3. otherwise `true`

### Scope discipline

Do not bloat this into a general deployment engine.

Version one should not include:

- MCP support
- assistant-specific routing
- config merging
- `.gitignore` management
- analyzer work
- preserve-user-edits mode
- transform engines
- template rendering
- partial per-entry override inside a tag

## Suggested current architecture

### Project layout

- `src/Payload`
  - the shared package with task + targets
- `tests/ParentPackage`
  - a parent-package fixture that declares bundled content
- `tests/ConsumerApp`
  - a consumer fixture showing opt-out usage

### Build package responsibilities

The shared build package should contain:

- an MSBuild task assembly
- pack-time generation for parent-package `.targets`
- a `.targets` file under `buildTransitive`
- helper logic for root detection
- helper logic for file enumeration and copy decisions

### Parent package responsibilities

A parent package that references the shared build package should:

- include its bundled content in the `.nupkg`
- author `PayloadContent` items in its project
- use `PayloadContent CopyOnBuild="false"` for optional-by-default payload groups when needed
- let `Payload` generate the `.targets` file during pack
- choose stable, package-specific tags such as:
  - `ExampleSkill`
  - `PrettyConsoleSkill`
  - `ArrowDbDocs`

### Consumer responsibilities

A consumer may declare policies such as:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage" Tag="ExampleSkill" CopyOnBuild="false" />
</ItemGroup>
```

Or opt a tag into absolute destination handling:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage" Tag="ExampleSkill" PathKind="Absolute" />
</ItemGroup>
```

## Immediate implementation priorities

1. get the XML contract stable
2. implement root detection
3. implement recursive file enumeration
4. implement SHA-256-based copy decision
5. implement consumer policy matching
6. wire everything through a `buildTransitive` target
7. keep the sample package and sample consumer simple and honest

## Remaining backlog

Some parts are expected to be completed by a follow-up agent or human:

- package readme polish for produced NuGet packages

## Working style for future agents

When continuing work here:

- do not reopen settled design questions unless implementation proves them impossible or dangerous
- prefer small boring changes over clever abstractions
- do not introduce IDs, MCP, or assistant matching
- do not add delete-on-disable behavior
- do not add `.gitignore` support in version one
- do not switch back to timestamps as the main comparison method
- keep docs aligned with the real behavior

If something cannot be implemented exactly as decided, explain the tradeoff plainly in code comments or README before changing the contract.
