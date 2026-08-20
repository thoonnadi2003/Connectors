[CmdletBinding()]
param(
	[Parameter()]
	[ValidateSet('binance', 'bybit', 'okx', 'bitmex', 'deribit', 'bitget', 'gate', 'phemex', 'blofin', 'gemini', 'coinbase', 'all')]
	[string[]] $Exchange = @('all'),

	[Parameter()]
	[ValidateSet('Debug', 'Release')]
	[string] $Configuration = 'Release',

	[Parameter()]
	[switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'DemoTrading.Tests\DemoTrading.Tests.csproj'
$originalSetting = [Environment]::GetEnvironmentVariable('STOCKSHARP_PUBLIC_TESTS', 'Process')
$tests = [ordered]@{
	binance = 'BinanceSpotTestnetOrderBook'
	bybit = 'BybitDemoOrderBook'
	okx = 'OkxPublicOrderBook'
	bitmex = 'BitmexTestnetOrderBook'
	deribit = 'DeribitTestnetOrderBook'
	bitget = 'BitgetPublicOrderBook'
	gate = 'GateFuturesTestnetOrderBook'
	phemex = 'PhemexTestnetOrderBook'
	blofin = 'BloFinDemoOrderBook'
	gemini = 'GeminiSandboxOrderBook'
	coinbase = 'CoinbaseStaticSandboxResponds'
}
$selected = if ($Exchange -contains 'all') { @($tests.Keys) } else { @($Exchange | Select-Object -Unique) }

try {
	[Environment]::SetEnvironmentVariable('STOCKSHARP_PUBLIC_TESTS', 'true', 'Process')
	$filter = ($selected | ForEach-Object { "FullyQualifiedName~DemoPublicEndpointTests.$($tests[$_])" }) -join '|'

	$arguments = @(
		'test'
		$projectPath
		'--configuration'
		$Configuration
		'--filter'
		$filter
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
