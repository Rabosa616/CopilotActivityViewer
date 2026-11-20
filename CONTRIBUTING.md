[code_of_conduct]: ./CODE_OF_CONDUCT.md
[security]: ./SECURITY.md
[specs]: ./specs/README.md
[issues]: https://github.com/Rabosa616/CopilotActivityViewer/issues
[fork]: https://docs.github.com/pull-requests/collaborating-with-pull-requests/working-with-forks/fork-a-repo
[pull]: https://docs.github.com/pull-requests/collaborating-with-pull-requests/proposing-changes-to-your-work-with-pull-requests/creating-a-pull-request-from-a-fork

# Contributing

Thanks for your interest in Copilot Activity Viewer! All contributions, however small, are
valued: bug reports, ideas, documentation, specs and code.

- [Before you start](#before-you-start)
- [Reporting bugs and requesting features](#reporting-bugs-and-requesting-features)
- [Development setup](#development-setup)
- [Making a change](#making-a-change)
- [Pull requests](#pull-requests)
- [Code of Conduct](#code-of-conduct)

## Before you start

- This project is released with a [Code of Conduct][code_of_conduct]. By participating you agree
  to abide by its terms.
- Everything in the repository is written in **English**: documentation, specs, code comments,
  commit messages and UI text.
- Do not report security vulnerabilities in public issues; follow the [Security Policy][security].

## Reporting bugs and requesting features

Use the [issue templates][issues]:

- **Bug report**: include the Visual Studio or SSMS version, the extension version and, if
  possible, the lines of `events.jsonl` that reproduce the problem (remove anything private).
- **Feature request**: describe the problem you want to solve before the solution.
- **Design proposal**: for larger changes that need a new or updated spec in [`specs/`][specs].

## Development setup

Requirements:

- Windows with the **.NET 10 SDK** or later.
- **Visual Studio 2026** (18.12 or later) or **SSMS 22** to try the extension.

```powershell
git clone https://github.com/<your-username>/CopilotActivityViewer.git
cd CopilotActivityViewer
dotnet build CopilotActivityViewer.slnx -c Release
dotnet test CopilotActivityViewer.slnx -c Release --no-build
```

The package is written to
`src\CopilotActivityViewer\bin\Release\net10.0-windows8.0\CopilotActivityViewer.vsix`. Close the
IDE and double-click it to install your build.

## Making a change

1. [**Fork**][fork] the repository and create a topic branch from `master`:

	```sh
	git checkout -b <topic-branch-name>
	```

2. Read the relevant spec in [`specs/`][specs]. If the behavior changes, update the spec and its
   acceptance criteria in the same pull request.
3. Add or update tests in `tests/CopilotActivityViewer.Tests`. Every acceptance criterion in the
   specs maps to a test.
4. Make sure the build has no warnings and all tests pass.
5. Keep changes focused: one fix or feature per pull request.

Do not bump the version or create tags; releases are made by the maintainer by pushing a
`vX.Y.Z` tag (see [Publishing](README.md#publishing)).

## Pull requests

Open a [pull request][pull] against `master` with a clear title and description, and fill in the
checklist of the template. CI builds the extension and runs the tests on every pull request.

## Code of Conduct

This project follows a [Code of Conduct][code_of_conduct] in order to ensure an open and
welcoming environment.
