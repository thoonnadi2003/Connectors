namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in OKX Demo Trading order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class OkxDemoTradingCycleTests
{
	private const string _instrument = "BTC-USDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("OKX_DEMO_API_KEY");
		var secret = EnvironmentVariable("OKX_DEMO_API_SECRET");
		var passphrase = EnvironmentVariable("OKX_DEMO_API_PASSPHRASE");

		using var client = new HttpClient { BaseAddress = new("https://www.okx.com") };
		using var instruments = await SendJson(client, HttpMethod.Get,
			$"/api/v5/public/instruments?instType=SPOT&instId={_instrument}", string.Empty, [], "OKX Demo");
		RequireSuccess(instruments.RootElement);
		var instrument = instruments.RootElement.GetProperty("data")[0];

		using var books = await SendJson(client, HttpMethod.Get,
			$"/api/v5/market/books?instId={_instrument}&sz=5", string.Empty, [], "OKX Demo");
		RequireSuccess(books.RootElement);
		var book = books.RootElement.GetProperty("data")[0];
		var bestBid = Decimal(book.GetProperty("bids")[0][0]);
		var bestAsk = Decimal(book.GetProperty("asks")[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid OKX order book.");

		using var balance = await SendSigned(client, HttpMethod.Get, "/api/v5/account/balance", string.Empty,
			key, secret, passphrase);
		RequireSuccess(balance.RootElement);

		var tickSize = Decimal(instrument.GetProperty("tickSz"));
		var lotSize = Decimal(instrument.GetProperty("lotSz"));
		var minSize = Decimal(instrument.GetProperty("minSz"));
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var quantity = Math.Max(minSize, CeilingToStep(5.25m / price, lotSize));
		var clientOrderId = ClientOrderId("ssokx");
		string orderId = null;

		try
		{
			var body = Json(new
			{
				instId = _instrument,
				tdMode = "cash",
				clOrdId = clientOrderId,
				side = "buy",
				ordType = "limit",
				px = Format(price),
				sz = Format(quantity),
			});
			using var created = await SendSigned(client, HttpMethod.Post, "/api/v5/trade/order", body,
				key, secret, passphrase);
			RequireSuccess(created.RootElement);
			var result = created.RootElement.GetProperty("data")[0];
			Assert.AreEqual("0", result.GetProperty("sCode").GetString(), "OKX Demo rejected the order.");
			orderId = result.GetProperty("ordId").GetString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "OKX Demo did not return an order id.");

			using var queried = await SendSigned(client, HttpMethod.Get,
				$"/api/v5/trade/order?instId={_instrument}&ordId={Uri.EscapeDataString(orderId!)}", string.Empty,
				key, secret, passphrase);
			RequireSuccess(queried.RootElement);
			Assert.AreEqual(orderId, queried.RootElement.GetProperty("data")[0].GetProperty("ordId").GetString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				var cancelBody = Json(new { instId = _instrument, ordId = orderId });
				using var canceled = await SendSigned(client, HttpMethod.Post, "/api/v5/trade/cancel-order", cancelBody,
					key, secret, passphrase);
				RequireSuccess(canceled.RootElement);
				Assert.AreEqual("0", canceled.RootElement.GetProperty("data")[0].GetProperty("sCode").GetString());
			}
		}
	}

	private static async Task<JsonDocument> SendSigned(HttpClient client, HttpMethod method, string path,
		string body, string key, string secret, string passphrase)
	{
		var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
		var signature = HmacSha256Base64(secret, timestamp + method.Method.ToUpperInvariant() + path + body);
		var headers = new List<(string, string)>
		{
			("OK-ACCESS-KEY", key), ("OK-ACCESS-SIGN", signature),
			("OK-ACCESS-TIMESTAMP", timestamp), ("OK-ACCESS-PASSPHRASE", passphrase),
			("x-simulated-trading", "1"),
		};
		return await SendJson(client, method, path, body, headers, "OKX Demo");
	}

	private static void RequireSuccess(JsonElement root)
		=> Assert.AreEqual("0", root.GetProperty("code").GetString(), "OKX Demo rejected the request.");
}
