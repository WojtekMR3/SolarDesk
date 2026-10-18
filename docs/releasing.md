# Making a release

Run these steps on Windows with a .NET 8 SDK (8.0.4xx). Keep the working tree clean and review the files included in the commit. User settings, logs, diagnostics, local SDKs, and build outputs must not be committed.

1. Update the version in `src/SolarDesk.App/SolarDesk.App.csproj` and the release notes in `CHANGELOG.md`.
2. Run the Windows build workflow in GitHub Actions. Its `./package.ps1` step builds a standalone executable, runs core tests and UI checks against that executable, then creates a ZIP and SHA-256 checksum in `artifacts/releases`. Download the `SunsetMonitor-win-x64` workflow artifact for publication. Local `test.ps1` runs only core checks; UI tests show and focus windows and must not run on an active working desktop. A dedicated test desktop can explicitly opt in with `./package.ps1 -IncludeUi`.
3. Inspect the ZIP. It should contain the executable, documentation, licenses, and data attribution. Test the extracted app on Windows with compatible hardware before claiming new hardware support. Automated checks use simulated monitors.
4. Push the reviewed commit and wait for the Windows build workflow to pass.
5. Create a version tag at that commit and publish a GitHub release with the ZIP and checksum. Use the version's notes from `CHANGELOG.md`; identify known limitations, including unsigned builds.
6. After the release is published, update both direct ZIP links and the displayed version in `README.md`. Keep the download button linked to `/releases/download/v<version>/SunsetMonitor-<version>-win-x64.zip` so one click starts the download. Include the same direct download link at the top of the release notes, and verify it before pushing the README update.

Example (replace the version and notes filename):

```powershell
git tag v2.9.7
git push origin v2.9.7
gh release create v2.9.7 --verify-tag --title "Sunset Monitor 2.9.7" --notes-file release-notes.txt artifacts/releases/SunsetMonitor-2.9.7-win-x64.zip artifacts/releases/SunsetMonitor-2.9.7-win-x64.zip.sha256
```

Do not overwrite published release assets with changed bytes. Publish a new version instead. There is no automatic in-app updater; users replace the extracted files after quitting from the tray menu.

## Build automation

The GitHub workflow runs on pushes, pull requests, and manual dispatch. It uses read-only repository permissions, builds and tests Windows x64, and uploads the ZIP, checksum, and simulated UI report as workflow artifacts. It does not publish releases automatically.

## Verify a download

```powershell
Get-FileHash .\SunsetMonitor-2.9.7-win-x64.zip -Algorithm SHA256
Get-Content .\SunsetMonitor-2.9.7-win-x64.zip.sha256
```

The values should match. The checksum detects a damaged or changed download; it is not a code-signing certificate.
