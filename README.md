# Payload

`Payload` is a small build-time NuGet helper that copies package-bundled files into a consumer repository during build.

It is intentionally narrow:

- parent package authors declare grouped copy instructions with `PayloadContent`
- consumer projects declare `PayloadPolicy` items scoped by `PackageId + Tag`
- `Payload` generates the parent package's `build` and `buildTransitive` `.targets` files during pack
- packaged files are copied into the consumer repository root on build
- disabling stops future synchronization but does not remove existing files
- file comparison uses size + SHA-256, not timestamps
- consumer projects may set `PayloadRootDirectory` explicitly to bypass root detection
- consumer paths are relative by default and can opt into absolute destinations with `PathKind="Absolute"`

Authoring shape:

```xml
<ItemGroup>
  <PayloadContent Include="content/skills/fluent-validation-expert">
    <Tag>FluentValidationSkill</Tag>
    <TargetPath>.agents/skills/fluent-validation-expert</TargetPath>
  </PayloadContent>
</ItemGroup>
```

Consumer shape:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage.Example" Tag="FluentValidationSkill" Disable="true" />
</ItemGroup>
```

Absolute path opt-in:

```xml
<ItemGroup>
  <PayloadPolicy Include="ParentPackage.Example" Tag="FluentValidationSkill" PathKind="Absolute" />
</ItemGroup>
```

`PathKind` is optional. Supported values are `Relative` and `Absolute`.

- `Relative` is the default and requires `TargetPath` to stay non-rooted
- `Absolute` allows rooted `TargetPath` values and does not depend on repository-root detection

## Included Projects

- `src/Payload` - the shared build package scaffold
- `samples/ParentPackage.Example` - example parent package that ships a skill
- `samples/ConsumerApp` - example consumer with opt-out

## Status

The current implementation is working end to end:

- `Payload` targets `netstandard2.0`
- parent package assets and transitive `.targets` files are generated during pack
- consumer builds import `Payload` through `buildTransitive`
- file and directory payloads are copied relative to a detected repository root
- absolute destination paths are supported through `PayloadPolicy PathKind="Absolute"`
- consumer policies disable copying per `PackageId + Tag`
- the repo has unit and integration coverage with TUnit
- the `Payload` package readme is wired to the repository root `README.md`

Typical verification command:

```bash
dotnet run --project tests/Payload.Tests/Payload.Tests.csproj -- --disable-logo
```

## Remaining Backlog

- package readme polish for produced NuGet packages
