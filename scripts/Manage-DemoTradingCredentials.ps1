[CmdletBinding()]
param(
	[Parameter()]
	[ValidateSet('binance', 'bybit', 'okx', 'bitmex', 'deribit', 'bitget', 'fxcm', 'gate', 'phemex', 'blofin', 'deriv', 'gemini', 'all')]
	[string[]] $Exchange = @('all'),

	[Parameter()]
	[ValidateSet('Status', 'Set', 'Clear')]
	[string] $Action = 'Status',

	[Parameter()]
	[switch] $Force
)

$ErrorActionPreference = 'Stop'

$config = [ordered]@{
	binance = @('BINANCE_TESTNET_API_KEY', 'BINANCE_TESTNET_API_SECRET')
	bybit = @('BYBIT_DEMO_API_KEY', 'BYBIT_DEMO_API_SECRET')
	okx = @('OKX_DEMO_API_KEY', 'OKX_DEMO_API_SECRET', 'OKX_DEMO_API_PASSPHRASE')
	bitmex = @('BITMEX_TESTNET_API_KEY', 'BITMEX_TESTNET_API_SECRET')
	deribit = @('DERIBIT_TESTNET_CLIENT_ID', 'DERIBIT_TESTNET_CLIENT_SECRET')
	bitget = @('BITGET_DEMO_API_KEY', 'BITGET_DEMO_API_SECRET', 'BITGET_DEMO_API_PASSPHRASE')
	fxcm = @('FXCM_DEMO_LOGIN', 'FXCM_DEMO_PASSWORD')
	gate = @('GATE_TESTNET_API_KEY', 'GATE_TESTNET_API_SECRET')
	phemex = @('PHEMEX_TESTNET_API_KEY', 'PHEMEX_TESTNET_API_SECRET')
	blofin = @('BLOFIN_DEMO_API_KEY', 'BLOFIN_DEMO_API_SECRET', 'BLOFIN_DEMO_API_PASSPHRASE')
	deriv = @('DERIV_DEMO_APP_ID', 'DERIV_DEMO_API_TOKEN')
	gemini = @('GEMINI_SANDBOX_API_KEY', 'GEMINI_SANDBOX_API_SECRET')
}

$selected = if ($Exchange -contains 'all') {
	@($config.Keys)
}
else {
	@($Exchange | Select-Object -Unique)
}

function Get-PlainText {
	param([Security.SecureString] $SecureValue)

	$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)

	try {
		[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
	}
	finally {
		[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
	}
}

switch ($Action) {
	'Status' {
		foreach ($name in $selected) {
			$variables = $config[$name]
			$configured = @($variables | Where-Object {
				-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'User'))
			})

			[pscustomobject]@{
				Exchange = $name
				Configured = $configured.Count
				Required = $variables.Count
				Ready = $configured.Count -eq $variables.Count
			}
		}

		break
	}
	'Set' {
		foreach ($name in $selected) {
			Write-Host "Configuring $name credentials. Input is hidden."

			foreach ($variable in $config[$name]) {
				$secureValue = Read-Host -Prompt $variable -AsSecureString
				$value = (Get-PlainText $secureValue).Trim()

				try {
					if ([string]::IsNullOrWhiteSpace($value)) {
						throw "$variable cannot be empty."
					}

					[Environment]::SetEnvironmentVariable($variable, $value, 'User')
				}
				finally {
					$value = $null
					$secureValue.Dispose()
				}
			}

			Write-Host "$name credentials saved as Windows user environment variables."
		}

		break
	}
	'Clear' {
		if (-not $Force) {
			throw 'Clearing credentials requires -Force.'
		}

		foreach ($name in $selected) {
			foreach ($variable in $config[$name]) {
				[Environment]::SetEnvironmentVariable($variable, $null, 'User')
			}

			Write-Host "$name user environment variables cleared."
		}
	}
}
