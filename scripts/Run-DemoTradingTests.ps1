[CmdletBinding()]
param(
	[Parameter()]
	[ValidateSet('binance', 'bybit', 'okx', 'bitmex', 'deribit', 'bitget', 'all')]
	[string[]] $Exchange = @('binance'),

	[Parameter()]
	[ValidateSet('Debug', 'Release')]
	[string] $Configuration = 'Release',

	[Parameter()]
	[switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'DemoTrading.Tests\DemoTrading.Tests.csproj'

$config = [ordered]@{
	binance = @{ Test = 'BinanceTradingCycleTests'; Vars = @('BINANCE_TESTNET_API_KEY', 'BINANCE_TESTNET_API_SECRET') }
	bybit = @{ Test = 'ByBitDemoTradingCycleTests'; Vars = @('BYBIT_DEMO_API_KEY', 'BYBIT_DEMO_API_SECRET') }
	okx = @{ Test = 'OkxDemoTradingCycleTests'; Vars = @('OKX_DEMO_API_KEY', 'OKX_DEMO_API_SECRET', 'OKX_DEMO_API_PASSPHRASE') }
	bitmex = @{ Test = 'BitmexTestnetTradingCycleTests'; Vars = @('BITMEX_TESTNET_API_KEY', 'BITMEX_TESTNET_API_SECRET') }
	deribit = @{ Test = 'DeribitTestnetTradingCycleTests'; Vars = @('DERIBIT_TESTNET_CLIENT_ID', 'DERIBIT_TESTNET_CLIENT_SECRET') }
	bitget = @{ Test = 'BitgetDemoTradingCycleTests'; Vars = @('BITGET_DEMO_API_KEY', 'BITGET_DEMO_API_SECRET', 'BITGET_DEMO_API_PASSPHRASE') }
}

$selected = if ($Exchange -contains 'all') {
	@($config.Keys)
}
else {
	@($Exchange | Select-Object -Unique)
}

$credentialValues = @{}
$missing = foreach ($name in $selected) {
	foreach ($variable in $config[$name].Vars) {
		$value = [Environment]::GetEnvironmentVariable($variable, 'Process')

		if ([string]::IsNullOrWhiteSpace($value)) {
			$value = [Environment]::GetEnvironmentVariable($variable, 'User')
		}

		if ([string]::IsNullOrWhiteSpace($value)) {
			$variable
		}
		else {
			$credentialValues[$variable] = $value
		}
	}
}

if ($missing) {
	throw "Configure the selected environment variables: $($missing -join ', ')."
}

$originalLiveSetting = [Environment]::GetEnvironmentVariable('STOCKSHARP_LIVE_TESTS', 'Process')
$originalCredentials = @{}

try {
	[Environment]::SetEnvironmentVariable('STOCKSHARP_LIVE_TESTS', 'true', 'Process')

	foreach ($pair in $credentialValues.GetEnumerator()) {
		$originalCredentials[$pair.Key] = [Environment]::GetEnvironmentVariable($pair.Key, 'Process')
		[Environment]::SetEnvironmentVariable($pair.Key, $pair.Value, 'Process')
	}

	$filter = ($selected | ForEach-Object { "FullyQualifiedName~$($config[$_].Test)" }) -join '|'
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
		throw "Demo trading tests failed with exit code $LASTEXITCODE."
	}
}
finally {
	[Environment]::SetEnvironmentVariable('STOCKSHARP_LIVE_TESTS', $originalLiveSetting, 'Process')

	foreach ($pair in $originalCredentials.GetEnumerator()) {
		[Environment]::SetEnvironmentVariable($pair.Key, $pair.Value, 'Process')
	}
}
