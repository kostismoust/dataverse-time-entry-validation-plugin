# Dataverse Project Operations Time Entry Validation Plugin

A sanitized Dynamics 365 Project Operations sample that validates project time entries server-side.

## What it does

- Blocks a project time entry when `msdyn_projecttask` is empty.
- Resolves a default Resource Category from the Bookable Resource when needed.
- Blocks the entry when the resolved role has no positive rate in a configured Cost Price List.
- Runs server-side, so the rule also applies to forms, grids, imports, Web API calls, and automations.

## Build

Requirements: .NET SDK with .NET Framework 4.6.2 targeting pack.

```bash
dotnet restore src/Dataverse.TimeEntryValidation/Dataverse.TimeEntryValidation.csproj
dotnet build src/Dataverse.TimeEntryValidation/Dataverse.TimeEntryValidation.csproj -c Release
```

For environments that require a strong-named assembly, generate a key locally and do not commit it:

```powershell
sn -k PluginKey.snk
dotnet build src/Dataverse.TimeEntryValidation/Dataverse.TimeEntryValidation.csproj -c Release `
  /p:SignAssembly=true /p:AssemblyOriginatorKeyFile="$PWD/PluginKey.snk"
```

## Registration

Register the assembly in sandbox mode, then add synchronous **PreOperation** steps:

| Message | Primary table | Filtering attributes | Pre-image |
|---|---|---|---|
| Create | `msdyn_timeentry` | — | — |
| Update | `msdyn_timeentry` | `msdyn_projecttask,msdyn_project,msdyn_bookableresource,msdyn_resourcecategory` | Name: `PreImage`; attributes: the same four columns |

Set the step's **unsecure configuration** to the GUID of the Cost Price List used for role-cost validation. Test in a sandbox before production.

## Security and portability

This repository intentionally contains no tenant URL, credentials, user names, customer names, environment IDs, solution export, binary output, Visual Studio cache, or signing key. Verify the logical names against your Project Operations version before deployment.

## License

No license is included. Add one only after confirming that you or your employer own the code and approve publication.
