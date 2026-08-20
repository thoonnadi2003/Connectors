namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Binance;
using StockSharp.Messages;

/// <summary>
/// Opt-in Binance Spot Testnet smoke test covering public market data and the
/// authenticated order lifecycle. Credentials are read only from environment variables.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BinanceTradingCycleTests
{
	private const string _baseUrl = "https://testnet.binance.vision";
	private const string _symbol = "BTCUSDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		var (key, secret) = GetCredentials();

		using var client = new HttpClient { BaseAddress = new(_baseUrl) };
		client.DefaultRequestHeaders.Add("X-MBX-APIKEY", key);
		var serverTimeOffset = await GetServerTimeOffset(client);

		var exchangeInfo = await GetJson(client, $"/api/v3/exchangeInfo?symbol={_symbol}");
		var symbol = exchangeInfo.RootElement.GetProperty("symbols")[0];
		var lotSize = symbol.GetProperty("filters").EnumerateArray()
			.First(f => f.GetProperty("filterType").GetString() == "LOT_SIZE");
		var priceFilter = symbol.GetProperty("filters").EnumerateArray()
			.First(f => f.GetProperty("filterType").GetString() == "PRICE_FILTER");
		var minNotionalFilter = symbol.GetProperty("filters").EnumerateArray()
			.FirstOrDefault(f => f.GetProperty("filterType").GetString() is "MIN_NOTIONAL" or "NOTIONAL");

		var depth = await GetJson(client, $"/api/v3/depth?symbol={_symbol}&limit=5");
		var bestBid = Decimal(depth.RootElement.GetProperty("bids")[0][0]);
		var bestAsk = Decimal(depth.RootElement.GetProperty("asks")[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid public order book.");

		await GetSignedJson(client, HttpMethod.Get, "/api/v3/account", [], secret, serverTimeOffset);

		var tickSize = Decimal(priceFilter.GetProperty("tickSize"));
		var stepSize = Decimal(lotSize.GetProperty("stepSize"));
		var minQty = Decimal(lotSize.GetProperty("minQty"));
		var minNotional = minNotionalFilter.ValueKind == JsonValueKind.Undefined
			? 10m
			: Decimal(minNotionalFilter.TryGetProperty("minNotional", out var mn) ? mn : minNotionalFilter.GetProperty("notional"));

		// Keep the order away from the spread so the test validates cancellation
		// without intentionally executing a trade.
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var quantity = Math.Max(minQty, CeilingToStep((minNotional * 1.05m) / price, stepSize));
		long? orderId = null;

		try
		{
			var created = await GetSignedJson(client, HttpMethod.Post, "/api/v3/order",
			[
				("symbol", _symbol), ("side", "BUY"), ("type", "LIMIT"),
				("timeInForce", "GTC"), ("quantity", Format(quantity)), ("price", Format(price)),
				("newOrderRespType", "RESULT")
			], secret, serverTimeOffset);

			orderId = created.RootElement.GetProperty("orderId").GetInt64();
			Assert.IsTrue(orderId > 0, "Testnet did not return an order id.");

			var queried = await GetSignedJson(client, HttpMethod.Get, "/api/v3/order",
			[("symbol", _symbol), ("orderId", orderId.Value.ToString(CultureInfo.InvariantCulture))], secret, serverTimeOffset);
			Assert.AreEqual(orderId.Value, queried.RootElement.GetProperty("orderId").GetInt64());
		}
		finally
		{
			if (orderId is not null)
			{
				var canceled = await GetSignedJson(client, HttpMethod.Delete, "/api/v3/order",
				[("symbol", _symbol), ("orderId", orderId.Value.ToString(CultureInfo.InvariantCulture))], secret, serverTimeOffset);
				Assert.AreEqual("CANCELED", canceled.RootElement.GetProperty("status").GetString());
			}
		}
	}

	[TestMethod]
	[Timeout(120000)]
	public async Task ConnectorWebSocketTradingCycle()
	{
		var (key, secret) = GetCredentials();

		using var client = new HttpClient { BaseAddress = new(_baseUrl) };
		var (price, quantity) = await GetSafeOrderAsync(client);

		var adapter = new BinanceMessageAdapter(new MillisecondIncrementalIdGenerator())
		{
			Key = key.Secure(),
			Secret = secret.Secure(),
			IsDemo = true,
			Sections = [BinanceSections.Spot],
			RemoveListenKeyOnDisconnect = false,
		};

		await using var harness = new MarketDataTestHarness(adapter);
		using var testSource = new CancellationTokenSource(TimeSpan.FromSeconds(105));
		var securityId = new SecurityId
		{
			SecurityCode = _symbol,
			BoardCode = BoardCodes.Binance,
		};
		var registerId = adapter.TransactionIdGenerator.GetNextId();
		long? orderId = null;
		var canceled = false;

		try
		{
			await harness.ConnectAsync(TimeSpan.FromSeconds(30), testSource.Token);

			var registeredReader = harness.CreateReader();
			harness.Post(new OrderRegisterMessage
			{
				TransactionId = registerId,
				SecurityId = securityId,
				Side = Sides.Buy,
				Price = price,
				Volume = quantity,
				OrderType = OrderTypes.Limit,
				TimeInForce = TimeInForce.PutInQueue,
				PostOnly = true,
			}, testSource.Token);

			var registered = await WaitForExecutionAsync(registeredReader,
				TimeSpan.FromSeconds(30), testSource.Token);

			if (registered.Error is not null)
				Assert.Fail(registered.Error.ToString());

			Assert.AreEqual(registerId, registered.OriginalTransactionId,
				"Binance WebSocket event was not correlated with the registration request.");
			Assert.IsTrue(registered.OrderState is OrderStates.Pending or OrderStates.Active,
				$"Unexpected registered order state: {registered.OrderState}.");
			Assert.IsNotNull(registered.OrderId, "Authenticated WebSocket event did not include the Binance order id.");
			orderId = registered.OrderId;

			var canceledReader = harness.CreateReader();
			var cancelId = PostCancel(harness, adapter, securityId, registerId, orderId, testSource.Token);

			var cancellation = await WaitForExecutionAsync(canceledReader,
				TimeSpan.FromSeconds(30), testSource.Token);

			if (cancellation.Error is not null)
				Assert.Fail(cancellation.Error.ToString());

			Assert.AreEqual(cancelId, cancellation.OriginalTransactionId,
				"Binance WebSocket event was not correlated with the cancellation request.");
			Assert.AreEqual(OrderStates.Done, cancellation.OrderState,
				$"Unexpected canceled order state: {cancellation.OrderState}.");
			canceled = true;
			Assert.AreEqual(0, harness.Errors.Length,
				"Binance adapter reported an out-of-band error during the authenticated trading cycle.");
		}
		finally
		{
			if (!canceled)
			{
				try
				{
					PostCancel(harness, adapter, securityId, registerId, orderId, CancellationToken.None);
					await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
				}
				catch (Exception)
				{
				}
			}

			try
			{
				await harness.DisconnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			}
			catch (Exception)
			{
			}
		}
	}

	private static (string key, string secret) GetCredentials()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
			Assert.Inconclusive("Set STOCKSHARP_LIVE_TESTS=true to run live integration tests.");

		var key = Environment.GetEnvironmentVariable("BINANCE_TESTNET_API_KEY");
		var secret = Environment.GetEnvironmentVariable("BINANCE_TESTNET_API_SECRET");

		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
			Assert.Inconclusive("Binance Testnet credentials are not configured in environment variables.");

		return (key, secret);
	}

	private static long PostCancel(MarketDataTestHarness harness, BinanceMessageAdapter adapter,
		SecurityId securityId, long registerId, long? orderId, CancellationToken cancellationToken)
	{
		var cancelId = adapter.TransactionIdGenerator.GetNextId();

		harness.Post(new OrderCancelMessage
		{
			TransactionId = cancelId,
			OriginalTransactionId = registerId,
			SecurityId = securityId,
			OrderId = orderId,
		}, cancellationToken);

		return cancelId;
	}

	private static async Task<ExecutionMessage> WaitForExecutionAsync(MessageReader reader,
		TimeSpan timeout, CancellationToken cancellationToken)
	{
		var message = await reader.WaitAsync<Message>(
			m => m is ErrorMessage or ExecutionMessage { HasOrderInfo: true },
			timeout, cancellationToken);

		if (message is ErrorMessage error)
			Assert.Fail(error.Error.ToString());

		return (ExecutionMessage)message;
	}

	private static async Task<(decimal price, decimal quantity)> GetSafeOrderAsync(HttpClient client)
	{
		var exchangeInfo = await GetJson(client, $"/api/v3/exchangeInfo?symbol={_symbol}");
		var symbol = exchangeInfo.RootElement.GetProperty("symbols")[0];
		var lotSize = symbol.GetProperty("filters").EnumerateArray()
			.First(f => f.GetProperty("filterType").GetString() == "LOT_SIZE");
		var priceFilter = symbol.GetProperty("filters").EnumerateArray()
			.First(f => f.GetProperty("filterType").GetString() == "PRICE_FILTER");
		var minNotionalFilter = symbol.GetProperty("filters").EnumerateArray()
			.FirstOrDefault(f => f.GetProperty("filterType").GetString() is "MIN_NOTIONAL" or "NOTIONAL");

		var depth = await GetJson(client, $"/api/v3/depth?symbol={_symbol}&limit=5");
		var bestBid = Decimal(depth.RootElement.GetProperty("bids")[0][0]);
		var bestAsk = Decimal(depth.RootElement.GetProperty("asks")[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid public order book.");

		var tickSize = Decimal(priceFilter.GetProperty("tickSize"));
		var stepSize = Decimal(lotSize.GetProperty("stepSize"));
		var minQty = Decimal(lotSize.GetProperty("minQty"));
		var minNotional = minNotionalFilter.ValueKind == JsonValueKind.Undefined
			? 10m
			: Decimal(minNotionalFilter.TryGetProperty("minNotional", out var mn) ? mn : minNotionalFilter.GetProperty("notional"));

		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var quantity = Math.Max(minQty, CeilingToStep((minNotional * 1.05m) / price, stepSize));
		return (price, quantity);
	}

	private static async Task<JsonDocument> GetJson(HttpClient client, string uri)
	{
		using var response = await client.GetAsync(uri);
		var body = await response.Content.ReadAsStringAsync();
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(body);
	}

	private static async Task<long> GetServerTimeOffset(HttpClient client)
	{
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		using var time = await GetJson(client, "/api/v3/time");
		var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var localMidpoint = before + ((after - before) / 2);
		return time.RootElement.GetProperty("serverTime").GetInt64() - localMidpoint;
	}

	private static async Task<JsonDocument> GetSignedJson(HttpClient client, HttpMethod method, string path,
		IEnumerable<(string key, string value)> parameters, string secret, long serverTimeOffset)
	{
		// Binance rejects timestamps more than 1000 ms ahead of its clock. Keep a
		// small backward safety margin for variable testnet latency and clock jitter.
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffset - 1000;
		var values = parameters
			.Append(("recvWindow", "10000"))
			.Append(("timestamp", timestamp.ToString(CultureInfo.InvariantCulture)));
		var query = string.Join("&", values.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
		var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
		using var request = new HttpRequestMessage(method, $"{path}?{query}&signature={signature}");
		using var response = await client.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
		{
			using var error = JsonDocument.Parse(body);
			var code = error.RootElement.TryGetProperty("code", out var codeValue) ? codeValue.ToString() : "unknown";
			var message = error.RootElement.TryGetProperty("msg", out var messageValue) ? messageValue.GetString() : "unknown";
			Assert.Fail($"Binance Testnet request failed with HTTP {(int)response.StatusCode} (code={code}, message={message}).");
		}
		return JsonDocument.Parse(body);
	}

	private static decimal Decimal(JsonElement value) => decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
	private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
	private static decimal FloorToStep(decimal value, decimal step) => Math.Floor(value / step) * step;
	private static decimal CeilingToStep(decimal value, decimal step) => Math.Ceiling(value / step) * step;
}
