# Demo and testnet trading tests

This repository contains opt-in automated trading-cycle tests for Binance plus five additional exchanges with official simulated environments: Bybit, OKX, BitMEX, Deribit, and Bitget. Each test reads public market data, authenticates, checks the account, places a non-marketable limit order, queries it, and attempts cancellation in a `finally` block.

The tests never read credentials from source files or `.env` files. Do not paste credentials into issues, pull requests, chat, screenshots, logs, or shell commands that will be retained in history. Use only simulated accounts and keys with read/trade permission; never grant withdrawal permission.

## Registration and API credentials

Availability and identity requirements depend on the exchange and jurisdiction. Follow the exchange's terms and do not attempt to bypass a location restriction.

### 1. Bybit Demo Trading

Official guide: [Demo Trading Service](https://bybit-exchange.github.io/docs/v5/demo)

1. Sign in to a regular Bybit mainnet account and complete any account verification required by Bybit.
2. Switch the account to **Demo Trading**.
3. In Demo Trading, open the avatar menu, choose **API**, and create an API key.
4. Enable account/order read and trade permissions only. Do not enable withdrawals.
5. Save the API key and secret when shown.
6. Do not create this key inside Bybit Testnet: Bybit Demo Trading is a separate environment and uses `https://api-demo.bybit.com`.

Environment variables:

- `BYBIT_DEMO_API_KEY`
- `BYBIT_DEMO_API_SECRET`

### 2. OKX Demo Trading

Official guide: [OKX API guide — Demo Trading Services](https://app.okx.com/docs-v5/en/)

1. Sign in to OKX and complete any verification required for the account.
2. Open **Trade → Demo Trading**.
3. Open **Personal Center → Demo Trading API**.
4. Create a Demo Trading API key with read and trade permissions only.
5. Create and securely save the API passphrase, API key, and secret.
6. Set the account mode once in the OKX web/app interface if OKX requests it. Every automated request adds `x-simulated-trading: 1`.

Environment variables:

- `OKX_DEMO_API_KEY`
- `OKX_DEMO_API_SECRET`
- `OKX_DEMO_API_PASSPHRASE`

### 3. BitMEX Testnet

Official guides: [BitMEX API access](https://support.bitmex.com/hc/en-gb/articles/6205448296605-Does-BitMEX-Have-An-API), [API account security](https://support.bitmex.com/hc/en-gb/articles/6133237456413-BitMEX-API-Account-Security)

1. Register a separate simulated account at [BitMEX Testnet](https://testnet.bitmex.com/).
2. Confirm the email and enable 2FA if offered.
3. Open **API Keys** at `https://testnet.bitmex.com/app/apiKeys`.
4. Create a key with order permission and without withdrawal permission.
5. Save the key ID and secret. The secret is shown only when the key is created.
6. Claim testnet funds from the faucet if the simulated account has no margin.

Environment variables:

- `BITMEX_TESTNET_API_KEY`
- `BITMEX_TESTNET_API_SECRET`

### 4. Deribit Testnet

Official guides: [Deribit Testnet](https://support.deribit.com/hc/en-us/articles/28685393662365-Deribit-Testnet), [API quickstart](https://docs.deribit.com/articles/deribit-quickstart), [Creating API keys](https://docs.deribit.com/articles/creating-api-key)

1. Register a separate account at [Deribit Testnet](https://test.deribit.com/). Production and testnet credentials are separate.
2. Confirm the account and log in to the test environment.
3. Open **Account → API** and create a key.
4. Grant `account:read` and `trade:read_write`; do not grant wallet withdrawal permissions.
5. Save the Client ID and Client Secret immediately. The secret cannot be retrieved later.
6. Use the testnet faucet to add simulated funds if required.

Environment variables:

- `DERIBIT_TESTNET_CLIENT_ID`
- `DERIBIT_TESTNET_CLIENT_SECRET`

### 5. Bitget Demo Trading

Official guides: [Bitget REST API Demo Trading](https://www.bitget.com/api-doc/classic/demotrading/restapi), [API quick start and security](https://www.bitget.com/api-doc/classic/quickStart/intro)

1. Sign in to Bitget and complete KYC if Bitget requires it for demo API access.
2. Switch the account to **Demo Trading** mode.
3. Open **Personal Center → API Key Management**.
4. Choose **Create Demo API Key** and grant read/trade permissions only.
5. Create and securely save the passphrase, API key, and secret.
6. Claim demo assets if the account has none. Demo private requests use the `paptrading: 1` header.

Environment variables:

- `BITGET_DEMO_API_KEY`
- `BITGET_DEMO_API_SECRET`
- `BITGET_DEMO_API_PASSPHRASE`

### Existing Binance Spot Test Network coverage

Use [Binance Spot Test Network](https://testnet.binance.vision/) to sign in and generate an HMAC API key. Store it as:

- `BINANCE_TESTNET_API_KEY`
- `BINANCE_TESTNET_API_SECRET`

Binance coverage includes both a direct signed REST lifecycle and a connector-level lifecycle. The connector test waits for authenticated WebSocket subscription acknowledgement, places a non-marketable order, correlates the execution event, cancels it, and verifies the cancellation event. It uses only Binance Spot Test Network endpoints.

## Run locally

Set `STOCKSHARP_LIVE_TESTS=true` and the variables for exactly the exchange being tested in the current process, then run one class. For example:

```powershell
dotnet test DemoTrading.Tests/DemoTrading.Tests.csproj --configuration Release --filter "FullyQualifiedName~ByBitDemoTradingCycleTests"
```

Use the Windows Environment Variables settings or a secure secret manager to inject the values. Clear process variables after the run. Do not create a credential file in the repository. `.env`, `.env.*`, and `*.secrets` are ignored as a final defense, but ignored plaintext is still not recommended.

## Run in GitHub Actions

Add the required names above under **Repository Settings → Secrets and variables → Actions → New repository secret**. Then run the **Demo trading cycles** workflow manually and select one exchange. The workflow checks only that exchange's required variable names and never prints their values.
