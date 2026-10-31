# dotnet-template
A skeleton template for bootstrapping new .NET repositories with a standardized project structure, development workflows, CI/CD foundations, and essential engineering practices already in place.

## Getting started (My Garage)

Prerequisites: .NET 10 SDK, [Aspire CLI](https://aspire.dev), Azure CLI, PowerShell 7+, and a trusted dev certificate (`dotnet dev-certs https --trust`).

1. Register the apps in your Entra tenant: see [infra/entra/README.md](infra/entra/README.md).
2. Start everything with `aspire run`, then browse to `https://localhost:7080`.
3. Run the tests with `dotnet test MyGarage.slnx`.

Full validation steps: [specs/001-app-foundation-auth/quickstart.md](specs/001-app-foundation-auth/quickstart.md).

## SpecKit

This template was set up with [SpecKit](https://github.com/github/spec-kit) for specification-driven development. Its configuration, templates, scripts, and workflows live in [`.specify/`](.specify/), with Copilot skills in `.github/skills/speckit-*`.

If you use SpecKit, start by replacing the placeholders in the [project constitution](.specify/memory/constitution.md) with your project's principles, then use the SpecKit skills to specify features, plan implementation, and generate tasks.

### Using this template without SpecKit

SpecKit is optional. In a project created from this template:

1. Preserve any project-specific principles or specifications you want to keep before removing SpecKit files.
2. Remove the `.specify/` directory.
3. Remove only the `speckit-*` directories under `.github/skills/`; keep any unrelated skills.
4. Update [`.github/copilot-instructions.md`](.github/copilot-instructions.md): remove the opening constitution reference and adjust the **Workflow** section to describe your preferred development process. Keep the remaining engineering guidance as appropriate.
5. Remove or update this README section and any remaining SpecKit references, including those in documentation, automation, or generated feature specifications.

The `docs/`, `infra/`, `src/`, and `tests/` structure can be used independently of SpecKit.
