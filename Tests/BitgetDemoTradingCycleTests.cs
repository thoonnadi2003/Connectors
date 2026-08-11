namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in Bitget Demo Trading order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class BitgetDemoTradingCycleTests
{
	private const string _symbol = "BTCUSDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("BITGET_DEMO_API_KEY");
		var secret = EnvironmentVariable("BITGET_DEMO_API_SECRET");
		var passphrase = EnvironmentVariable("BITGET_DEMO_API_PASSPHRASE");

		using var client = new HttpClient { BaseAddress = new("https://api.bitget.com") };
		using var symbols = await SendJson(client, HttpMethod.Get,
			"/api/v2/spot/public/symbols?symbol=" + _symbol, string.Empty, [], "Bitget Demo");
		RequireSuccess(symbols.RootElement);
		var instrument = symbols.RootElement.GetProperty("data")[0];

		using var orderBook = await SendJson(client, HttpMethod.Get,
			"/api/v2/spot/market/orderbook?symbol=" + _symbol + "&limit=5", string.Empty, [], "Bitget Demo");
		RequireSuccess(orderBook.RootElement);
		var book = orderBook.RootElement.GetProperty("data");
		var bestBid = Decimal(book.GetProperty("bids")[0][0]);
		var bestAsk = Decimal(book.GetProperty("asks")[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid Bitget order book.");

		using var account = await SendSigned(client, HttpMethod.Get, "/api/v2/spot/account/assets",
			string.Empty, key, secret, passphrase);
		RequireSuccess(account.RootElement);

		var pricePrecision = Integer(instrument.GetProperty("pricePrecision"));
		var quantityPrecision = Integer(instrument.GetProperty("quantityPrecision"));
		var priceStep = (decimal)Math.Pow(10, -pricePrecision);
		var quantityStep = (decimal)Math.Pow(10, -quantityPrecision);
		var minimumNotional = instrument.TryGetProperty("minTradeUSDT", out var min) ? Decimal(min) : 5m;
		var price = FloorToStep(bestBid * 0.8m, priceStep);
		var quantity = CeilingToStep(minimumNotional * 1.05m / price, quantityStep);
		var clientOrderId = ClientOrderId("ss-bg-");
		string orderId = null;

		try
		{
			var body = Json(new
			{
				symbol = _symbol,
				side = "buy",
				orderType = "limit",
				force = "gtc",
				price = Format(price),
				size = Format(quantity),
				clientOid = clientOrderId,
			});
			using var created = await SendSigned(client, HttpMethod.Post, "/api/v2/spot/trade/place-order", body,
				key, secret, passphrase);
			RequireSuccess(created.RootElement);
			orderId = created.RootElement.GetProperty("data").GetProperty("orderId").GetString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "Bitget Demo did not return an order id.");

			using var queried = await SendSigned(client, HttpMethod.Get,
				"/api/v2/spot/trade/orderInfo?orderId=" + Uri.EscapeDataString(orderId!), string.Empty,
				key, secret, passphrase);
			RequireSuccess(queried.RootElement);
			Assert.AreEqual(orderId, queried.RootElement.GetProperty("data").GetProperty("orderId").GetString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				var cancelBody = Json(new { symbol = _symbol, orderId });
				using var canceled = await SendSigned(client, HttpMethod.Post, "/api/v2/spot/trade/cancel-order", cancelBody,
					key, secret, passphrase);
				RequireSuccess(canceled.RootElement);
			}
		}
	}

	private static async Task<JsonDocument> SendSigned(HttpClient client, HttpMethod method, string path,
		string body, string key, string secret, string passphrase)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
		var signature = HmacSha256Base64(secret, timestamp + method.Method.ToUpperInvariant() + path + body);
		var headers = new List<(string, string)>
		{
			("ACCESS-KEY", key), ("ACCESS-SIGN", signature), ("ACCESS-TIMESTAMP", timestamp),
			("ACCESS-PASSPHRASE", passphrase), ("paptrading", "1"),
		};
		return await SendJson(client, method, path, body, headers, "Bitget Demo");
	}

	private static void RequireSuccess(JsonElement root)
		=> Assert.AreEqual("00000", root.GetProperty("code").GetString(), "Bitget Demo rejected the request.");
}
