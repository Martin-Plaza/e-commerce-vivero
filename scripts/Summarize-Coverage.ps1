param(
    [string]$ResultsDirectory = "TestResults"
)

$coverageFile = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter coverage.cobertura.xml |
    Select-Object -First 1

if (-not $coverageFile) {
    throw "No se encontro coverage.cobertura.xml bajo '$ResultsDirectory'."
}

[xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName
$rows = foreach ($package in $coverage.coverage.packages.package) {
    [pscustomobject]@{
        Proyecto = $package.name
        Lineas = "{0:N2}%" -f ([double]$package.'line-rate' * 100)
        Ramas = "{0:N2}%" -f ([double]$package.'branch-rate' * 100)
    }
}

$table = @(
    "## Cobertura por proyecto"
    ""
    "| Proyecto | Lineas | Ramas |"
    "|---|---:|---:|"
    $rows | ForEach-Object { "| $($_.Proyecto) | $($_.Lineas) | $($_.Ramas) |" }
) -join [Environment]::NewLine

$table
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $table
}
