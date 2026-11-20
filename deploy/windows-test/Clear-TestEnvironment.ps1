$inheritedSettings = '^(?:RHINO_|INSOLE_|VITE_|ConnectionStrings__|Jwt__|Stripe[-_]|ASPNETCORE_|DOTNET_ENVIRONMENT$|PGPASSWORD$|GITHUB_TOKEN$|GH_TOKEN$)'
foreach ($name in [Environment]::GetEnvironmentVariables().Keys) {
    if ($name -match $inheritedSettings) { [Environment]::SetEnvironmentVariable($name, $null) }
}
