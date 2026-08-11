namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in BitMEX Testnet order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class BitmexTestnetTradingCycleTests
{
	private const string _symbol = "XBTUSD";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("BITMEX_TESTNET_API_KEY");
		var secret = EnvironmentVariable("BITMEX_TESTNET_API_SECRET");

		using var client = new HttpClient { BaseAddress = new("https://testnet.bitmex.com") };
		using var instruments = await SendJson(client, HttpMethod.Get,
			$"/api/v1/instrument?symbol={_symbol}", string.Empty, [], "BitMEX Testnet");
		var instrument = instruments.RootElement[0];
		using var orderBook = await SendJson(client, HttpMethod.Get,
			$"/api/v1/orderBook/L2?symbol={_symbol}&depth=10", string.Empty, [], "BitMEX Testnet");
		var levels = orderBook.RootElement.EnumerateArray().ToArray();
		var bestBid = levels.Where(l => l.GetProperty("side").GetString() == "Buy").Max(l => Decimal(l.GetProperty("price")));
		var bestAsk = levels.Where(l => l.GetProperty("side").GetString() == "Sell").Min(l => Decimal(l.GetProperty("price")));
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid BitMEX Testnet order book.");

		using var margin = await SendSigned(client, HttpMethod.Get, "/api/v1/user/margin?currency=XBt",
			string.Empty, key, secret);
		Assert.IsTrue(margin.RootElement.TryGetProperty("currency", out _), "BitMEX Testnet margin response is invalid.");

		var tickSize = Decimal(instrument.GetProperty("tickSize"));
		var lotSize = instrument.TryGetProperty("lotSize", out var lot) ? Decimal(lot) : 100m;
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var clientOrderId = ClientOrderId("ss-bm-");
		string orderId = null;

		try
		{
			var body = Json(new
			{
				symbol = _symbol,
				side = "Buy",
				orderQty = lotSize,
				price,
				ordType = "Limit",
				timeInForce = "GoodTillCancel",
				execInst = "ParticipateDoNotInitiate",
				clOrdID = clientOrderId,
			});
			using var created = await SendSigned(client, HttpMethod.Post, "/api/v1/order", body, key, secret);
			orderId = created.RootElement.GetProperty("orderID").GetString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "BitMEX Testnet did not return an order id.");

			var filter = Uri.EscapeDataString(Json(new { orderID = orderId }));
			using var queried = await SendSigned(client, HttpMethod.Get, $"/api/v1/order?filter={filter}",
				string.Empty, key, secret);
			Assert.AreEqual(orderId, queried.RootElement[0].GetProperty("orderID").GetString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				using var canceled = await SendSigned(client, HttpMethod.Delete, "/api/v1/order",
					Json(new { orderID = orderId }), key, secret);
				Assert.AreEqual(orderId, canceled.RootElement[0].GetProperty("orderID").GetString());
			}
		}
	}

	private static async Task<JsonDocument> SendSigned(HttpClient client, HttpMethod method, string path,
		string body, string key, string secret)
	{
		var expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30;
		var signature = HmacSha256Hex(secret, method.Method.ToUpperInvariant() + path + expires + body);
		var headers = new List<(string, string)>
		{
			("api-key", key), ("api-expires", expires.ToString()), ("api-signature", signature),
		};
		return await SendJson(client, method, path, body, headers, "BitMEX Testnet");
	}
}
