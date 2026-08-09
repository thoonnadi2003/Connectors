namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

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
		if (!string.Equals(Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
			Assert.Inconclusive("Set STOCKSHARP_LIVE_TESTS=true to run live integration tests.");

		var key = Environment.GetEnvironmentVariable("BINANCE_TESTNET_API_KEY");
		var secret = Environment.GetEnvironmentVariable("BINANCE_TESTNET_API_SECRET");

		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
			Assert.Inconclusive("Binance Testnet credentials are not configured in environment variables.");

		using var client = new HttpClient { BaseAddress = new(_baseUrl) };
		client.DefaultRequestHeaders.Add("X-MBX-APIKEY", key);

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

		await GetSignedJson(client, HttpMethod.Get, "/api/v3/account", [], secret);

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
			], secret);

			orderId = created.RootElement.GetProperty("orderId").GetInt64();
			Assert.IsTrue(orderId > 0, "Testnet did not return an order id.");

			var queried = await GetSignedJson(client, HttpMethod.Get, "/api/v3/order",
			[("symbol", _symbol), ("orderId", orderId.Value.ToString(CultureInfo.InvariantCulture))], secret);
			Assert.AreEqual(orderId.Value, queried.RootElement.GetProperty("orderId").GetInt64());
		}
		finally
		{
			if (orderId is not null)
			{
				var canceled = await GetSignedJson(client, HttpMethod.Delete, "/api/v3/order",
				[("symbol", _symbol), ("orderId", orderId.Value.ToString(CultureInfo.InvariantCulture))], secret);
				Assert.AreEqual("CANCELED", canceled.RootElement.GetProperty("status").GetString());
			}
		}
	}

	private static async Task<JsonDocument> GetJson(HttpClient client, string uri)
	{
		using var response = await client.GetAsync(uri);
		var body = await response.Content.ReadAsStringAsync();
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(body);
	}

	private static async Task<JsonDocument> GetSignedJson(HttpClient client, HttpMethod method, string path,
		IEnumerable<(string key, string value)> parameters, string secret)
	{
		var values = parameters.Append(("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
		var query = string.Join("&", values.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
		var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
		using var request = new HttpRequestMessage(method, $"{path}?{query}&signature={signature}");
		using var response = await client.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
			Assert.Fail($"Binance Testnet request failed with HTTP {(int)response.StatusCode}.");
		return JsonDocument.Parse(body);
	}

	private static decimal Decimal(JsonElement value) => decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
	private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
	private static decimal FloorToStep(decimal value, decimal step) => Math.Floor(value / step) * step;
	private static decimal CeilingToStep(decimal value, decimal step) => Math.Ceiling(value / step) * step;
}
