# Security Policy

- [Supported versions](#supported-versions)
- [Reporting a vulnerability](#reporting-a-vulnerability)
- [Disclosure policy](#disclosure-policy)

## Supported versions

Only the latest release published on the
[Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=OscarVaqueroVies.CopilotActivityViewer)
and in [GitHub Releases](https://github.com/Rabosa616/CopilotActivityViewer/releases/latest)
receives security fixes.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report them privately through
[GitHub private vulnerability reporting](https://github.com/Rabosa616/CopilotActivityViewer/security/advisories/new)
(**Security** tab > **Report a vulnerability**). Include the affected version, the steps to
reproduce it and the impact you expect.

The maintainer will acknowledge the report, send a more detailed response with the next steps,
keep you informed of the progress towards a fix and may ask for additional information.

Report vulnerabilities in third-party packages to the people who maintain them.

## Disclosure policy

When a report is received, the maintainer will:

- Confirm the problem and determine the affected versions.
- Audit the code to find any similar problems.
- Prepare a fix and release it as fast as possible, then publish a security advisory crediting
  the reporter (unless they prefer to stay anonymous).

Keep in mind that the extension only reads local Copilot session logs and sends nothing over the
network; reports about how it reads or displays those files are especially welcome.
