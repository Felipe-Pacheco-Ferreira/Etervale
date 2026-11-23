# Windows launcher

Run the launcher with PowerShell in STA mode and supply the path to an existing client executable with `-GamePath`.
Supply `-UpdateFeed` to enable the update button. The HTTPS endpoint returns JSON with `version`, `url` and `sha256`.
Use an increasing numeric version, an HTTPS URL for a Windows client ZIP and the ZIP's SHA-256 digest.
The ZIP root contains `PristonTale.exe` and the files that the Windows client needs.

The launcher verifies each download, extracts it in a staging directory and moves it into `versions/<version>`
after checking the executable. Play uses the newest installed version, falling back to the supplied game path.
Keep the launcher in a writable directory. The update endpoint and ZIP hosting are configured separately.
The local site serves the Windows release from wwwroot/updates/windows. The launcher can run the same update and launch flow with -UpdateOnly -PlayAfterUpdate for verification. The client ZIP excludes original game assets, which the game can resolve from the existing installation.
The download bar uses bytes received and the response Content-Length. When the total is unavailable, or while verifying or extracting files, the bar shows indeterminate progress.
