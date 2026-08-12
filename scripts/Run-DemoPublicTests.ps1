[CmdletBinding()]
param(
	[Parameter()]
	[ValidateSet('Debug', 'Release')]
	[string] $Configuration = 'Release',

	[Parameter()]
	[switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'DemoTrading.Tests\DemoTrading.Tests.csproj'
$originalSetting = [Environment]::GetEnvironmentVariable('STOCKSHARP_PUBLIC_TESTS', 'Process')

try {
	[Environment]::SetEnvironmentVariable('STOCKSHARP_PUBLIC_TESTS', 'true', 'Process')

	$arguments = @(
		'test'
		$projectPath
		'--configuration'
		$Configuration
		'--filter'
		'FullyQualifiedName~DemoPublicEndpointTests'
	)

	if ($NoRestore) {
		$arguments += '--no-restore'
	}

	& dotnet @arguments

	if ($LASTEXITCODE -ne 0) {
		throw "Public demo endpoint tests failed with exit code $LASTEXITCODE."
	}
}
finally {
	[Environment]::SetEnvironmentVariable('STOCKSHARP_PUBLIC_TESTS', $originalSetting, 'Process')
}
