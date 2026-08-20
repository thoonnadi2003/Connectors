namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in Gemini Sandbox order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class GeminiSandboxTradingCycleTests
{
	private const string _symbol = "btcusd";
	private static long _lastNonce;

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("GEMINI_SANDBOX_API_KEY");
		var secret = EnvironmentVariable("GEMINI_SANDBOX_API_SECRET");
		using var client = new HttpClient { BaseAddress = new("https://api.sandbox.gemini.com") };

		using var detailsResponse = await SendJson(client, HttpMethod.Get,
			$"/v1/symbols/details/{_symbol}", string.Empty, [], "Gemini Sandbox");
		var details = detailsResponse.RootElement;
		using var bookResponse = await SendJson(client, HttpMethod.Get,
			$"/v1/book/{_symbol}?limit_bids=5&limit_asks=5", string.Empty, [], "Gemini Sandbox");
		var bestBid = Decimal(bookResponse.RootElement.GetProperty("bids")[0].GetProperty("price"));
		Assert.IsTrue(bestBid > 0, "Gemini Sandbox returned an invalid bid.");

		using var balances = await SendPrivate(client, "/v1/balances", new { }, key, secret);
		Assert.AreEqual(JsonValueKind.Array, balances.RootElement.ValueKind, "Gemini Sandbox balance response changed.");

		var tickSize = details.TryGetProperty("quote_increment", out var quoteIncrement)
			? Decimal(quoteIncrement)
			: 0.01m;
		var minOrderSize = Decimal(details.GetProperty("min_order_size"));
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		string orderId = null;

		try
		{
			using var created = await SendPrivate(client, "/v1/order/new", new
			{
				symbol = _symbol,
				amount = Format(minOrderSize),
				price = Format(price),
				side = "buy",
				type = "exchange limit",
				options = new[] { "maker-or-cancel" },
				client_order_id = ClientOrderId("ss-gem-"),
			}, key, secret);
			orderId = created.RootElement.GetProperty("order_id").ToString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "Gemini Sandbox did not return an order id.");

			using var queried = await SendPrivate(client, "/v1/order/status", new { order_id = orderId }, key, secret);
			Assert.AreEqual(orderId, queried.RootElement.GetProperty("order_id").ToString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				using var canceled = await SendPrivate(client, "/v1/order/cancel", new { order_id = orderId }, key, secret);
				Assert.AreEqual(orderId, canceled.RootElement.GetProperty("order_id").ToString());
			}
		}
	}

	private static async Task<JsonDocument> SendPrivate(HttpClient client, string path, object parameters,
		string key, string secret)
	{
		var nonce = NextNonce();
		var values = JsonSerializer.Deserialize<Dictionary<string, object>>(Json(parameters)) ?? [];
		values["request"] = path;
		values["nonce"] = nonce;
		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json(values)));
		var headers = new List<(string, string)>
		{
			("X-GEMINI-APIKEY", key),
			("X-GEMINI-PAYLOAD", payload),
			("X-GEMINI-SIGNATURE", HmacSha384Hex(secret, payload)),
			("Cache-Control", "no-cache"),
		};
		return await SendJson(client, HttpMethod.Post, path, string.Empty, headers, "Gemini Sandbox");
	}

	private static long NextNonce()
	{
		while (true)
		{
			var previous = Volatile.Read(ref _lastNonce);
			var next = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), previous + 1);
			if (Interlocked.CompareExchange(ref _lastNonce, next, previous) == previous)
				return next;
		}
	}
}
