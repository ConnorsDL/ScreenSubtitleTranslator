# Security Policy

## Supported Versions

Security fixes are currently considered for the latest `0.1.x` release candidate only.

## Reporting a Vulnerability

Please report security issues privately. After the GitHub repository is published, use the repository's **Security > Report a vulnerability** flow when available. Do not open a public issue containing exploit details, credentials, private transcripts, or personal information.

Include a concise description, affected version, reproduction steps, expected impact, and any suggested mitigation. Maintainers will acknowledge the report and coordinate disclosure after the issue has been assessed.

## API Keys

Never commit, upload, paste, or attach a real OpenAI API Key. This includes source files, `.env`, `settings.json`, logs, screenshots, GitHub Issues, pull requests, release archives, and test artifacts.

The application reads `OPENAI_API_KEY` from the Windows environment. Only `.env.example` with a non-secret placeholder belongs in the repository. If a real key is exposed, revoke or rotate it immediately in the OpenAI platform and inspect account usage.

## Logs and Transcripts

Diagnostic logging is disabled by default. When explicitly enabled, logs may contain recognized speech and translated text. Remove private content before sharing a log with maintainers.
