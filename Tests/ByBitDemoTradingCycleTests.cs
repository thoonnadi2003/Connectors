namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in Bybit Demo Trading order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class ByBitDemoTradingCycleTests
{
	private const string _symbol = "BTCUSDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("BYBIT_DEMO_API_KEY");
		var secret = EnvironmentVariable("BYBIT_DEMO_API_SECRET");

		using var client = new HttpClient { BaseAddress = new("https://api-demo.bybit.com") };
		using var instrumentResponse = await SendJson(client, HttpMethod.Get,
			$"/v5/market/instruments-info?category=spot&symbol={_symbol}", string.Empty, [], "Bybit Demo");
		RequireSuccess(instrumentResponse.RootElement);
		var instrument = instrumentResponse.RootElement.GetProperty("result").GetProperty("list")[0];

		using var bookResponse = await SendJson(client, HttpMethod.Get,
			$"/v5/market/orderbook?category=spot&symbol={_symbol}&limit=5", string.Empty, [], "Bybit Demo");
		RequireSuccess(bookResponse.RootElement);
		var book = bookResponse.RootElement.GetProperty("result");
		var bestBid = Decimal(book.GetProperty("b")[0][0]);
		var bestAsk = Decimal(book.GetProperty("a")[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid Bybit Demo order book.");

		using var wallet = await SendSigned(client, HttpMethod.Get,
			"/v5/account/wallet-balance?accountType=UNIFIED", string.Empty, key, secret);
		RequireSuccess(wallet.RootElement);

		var priceFilter = instrument.GetProperty("priceFilter");
		var lotFilter = instrument.GetProperty("lotSizeFilter");
		var tickSize = Decimal(priceFilter.GetProperty("tickSize"));
		var quantityStep = Decimal(lotFilter.GetProperty("basePrecision"));
		var minQuantity = Decimal(lotFilter.GetProperty("minOrderQty"));
		var minNotional = lotFilter.TryGetProperty("minOrderAmt", out var minimum) ? Decimal(minimum) : 5m;
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var quantity = Math.Max(minQuantity, CeilingToStep(minNotional * 1.05m / price, quantityStep));
		var clientOrderId = ClientOrderId("ss-by-");
		string orderId = null;

		try
		{
			var body = Json(new
			{
				category = "spot",
				symbol = _symbol,
				side = "Buy",
				orderType = "Limit",
				qty = Format(quantity),
				price = Format(price),
				timeInForce = "GTC",
				orderLinkId = clientOrderId,
			});

			using var created = await SendSigned(client, HttpMethod.Post, "/v5/order/create", body, key, secret);
			RequireSuccess(created.RootElement);
			orderId = created.RootElement.GetProperty("result").GetProperty("orderId").GetString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "Bybit Demo did not return an order id.");

			using var queried = await SendSigned(client, HttpMethod.Get,
				$"/v5/order/realtime?category=spot&orderId={Uri.EscapeDataString(orderId!)}", string.Empty, key, secret);
			RequireSuccess(queried.RootElement);
			Assert.AreEqual(orderId, queried.RootElement.GetProperty("result").GetProperty("list")[0].GetProperty("orderId").GetString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				var cancelBody = Json(new { category = "spot", symbol = _symbol, orderId });
				using var canceled = await SendSigned(client, HttpMethod.Post, "/v5/order/cancel", cancelBody, key, secret);
				RequireSuccess(canceled.RootElement);
			}
		}
	}

	private static async Task<JsonDocument> SendSigned(HttpClient client, HttpMethod method, string path,
		string body, string key, string secret)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
		const string recvWindow = "5000";
		var signedValue = method == HttpMethod.Get && path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : body;
		var signature = HmacSha256Hex(secret, timestamp + key + recvWindow + signedValue);
		var headers = new List<(string, string)>
		{
			("X-BAPI-API-KEY", key), ("X-BAPI-SIGN", signature),
			("X-BAPI-TIMESTAMP", timestamp), ("X-BAPI-RECV-WINDOW", recvWindow),
		};
		return await SendJson(client, method, path, body, headers, "Bybit Demo");
	}

	private static void RequireSuccess(JsonElement root)
		=> Assert.AreEqual(0, root.GetProperty("retCode").GetInt32(), "Bybit Demo rejected the request.");
}
